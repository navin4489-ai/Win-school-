import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import 'auth_controller.dart';
import 'background_notify.dart';
import 'notify.dart';

/// Listens to the server's notification stream while the app is signed in.
///
/// Every notification rings a local notification with sound (so the bell works
/// even when the app is in the background) and updates [unread], which the
/// bell badge on the home screen shows. The connection is kept alive by the
/// server; when it drops, the feed reconnects on its own.
class LiveNotifications extends ChangeNotifier {
  LiveNotifications._();

  static final LiveNotifications instance = LiveNotifications._();

  /// The website's static proxy buffers server-sent events, so the live stream
  /// connects to the API service directly. It is the same backend, so the
  /// token issued by a normal sign-in works there too.
  static const _streamBase = String.fromEnvironment(
    'API_STREAM_URL',
    defaultValue: 'https://school-erp-api-3j9e.onrender.com/api',
  );

  AuthController? _auth;
  int _session = 0;
  bool _running = false;

  int unread = 0;
  bool get running => _running;

  /// Starts the feed. Safe to call again: only one loop runs at a time.
  Future<void> start(AuthController auth) async {
    _auth = auth;
    if (_running) return;
    _running = true;
    final session = ++_session;
    notifyListeners();

    unawaited(_pollUnread(session));
    await _run(session);
  }

  /// Stops the feed (called when the user signs out).
  void stop() {
    _session++;
    _running = false;
    unread = 0;
    notifyListeners();
  }

  /// Re-reads the unread count, e.g. after the notifications screen is closed.
  Future<void> refreshUnread() => _pollUnread(_session);

  Future<void> _run(int session) async {
    var delay = const Duration(seconds: 2);

    while (_running && session == _session) {
      try {
        await _connect(session);
        delay = const Duration(seconds: 2);
      } catch (_) {
        // Reconnect below.
      }
      if (!_running || session != _session) break;
      await Future<void>.delayed(delay);
      if (delay < const Duration(seconds: 30)) {
        delay *= 2;
      }
    }
  }

  Future<void> _connect(int session) async {
    final auth = _auth;
    if (auth == null) return;

    final token = await _accessToken(auth);
    if (token == null || !_running || session != _session) return;

    final client = http.Client();
    try {
      final request = http.Request(
        'GET',
        Uri.parse('$_streamBase/notifications/stream'),
      );
      request.headers['Authorization'] = 'Bearer $token';
      request.headers['Accept'] = 'text/event-stream';

      final response = await client.send(request).timeout(
        const Duration(seconds: 45),
      );
      if (response.statusCode == 401) {
        // The access token expired while the app was idle; renew it and let
        // the caller reconnect.
        await auth.freshAccessToken();
        return;
      }
      if (response.statusCode != 200) {
        return;
      }

      var event = '';
      var data = '';
      final lines =
          response.stream.transform(utf8.decoder).transform(const LineSplitter());

      await for (final line in lines) {
        if (!_running || session != _session) return;
        if (line.isEmpty) {
          if (event == 'notification' && data.isNotEmpty) {
            await _onNotification(data);
          }
          event = '';
          data = '';
        } else if (line.startsWith('event:')) {
          event = line.substring(6).trim();
        } else if (line.startsWith('data:')) {
          data = line.substring(5).trim();
        }
      }
    } finally {
      client.close();
    }
  }

  Future<void> _onNotification(String raw) async {
    try {
      final json = jsonDecode(raw) as Map<String, dynamic>;
      final id = int.tryParse('${json['id']}') ?? 0;
      final title = json['title']?.toString() ?? 'School notification';
      final message = json['message']?.toString() ?? '';
      await Notify.show(title, message);
      await _markSeen(id);
      await _refreshUnread();
    } catch (_) {
      // A malformed frame must not kill the stream.
    }
  }

  /// Remembers the newest notification the app has rung, so the background
  /// poll does not ring it a second time.
  Future<void> _markSeen(int id) async {
    if (id <= 0) return;
    try {
      final prefs = await SharedPreferences.getInstance();
      final seen = prefs.getInt('school_notify_seen') ?? 0;
      if (id > seen) await prefs.setInt('school_notify_seen', id);
    } catch (_) {
      // Ignore; a duplicate ring is better than a broken stream.
    }
  }

  Future<void> _pollUnread(int session) async {
    while (_running && session == _session) {
      await BackgroundNotify.heartbeat();
      await _refreshUnread();
      await Future<void>.delayed(const Duration(seconds: 60));
    }
  }

  Future<void> _refreshUnread() async {
    final auth = _auth;
    if (auth == null) return;
    try {
      final response = await auth.api.get('notifications/unread-count');
      final count = response is int ? response : int.tryParse('$response');
      if (count == null) return;
      if (count != unread) {
        unread = count;
        notifyListeners();
      }
    } catch (_) {
      // Offline or signed out; the next poll catches up.
    }
  }

  Future<String?> _accessToken(AuthController auth) async {
    final current = auth.api.accessToken;
    if (current != null) return current;
    await auth.freshAccessToken();
    return auth.api.accessToken;
  }
}
