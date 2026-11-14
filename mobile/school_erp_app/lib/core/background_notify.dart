import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import 'package:workmanager/workmanager.dart';

import 'notify.dart';

/// Keeps the notification bell working when the app is not open.
///
/// The server has no push channel (Firebase is not configured), so the app
/// checks for new notifications in the background every ~15 minutes and rings
/// a local notification with sound for anything that arrived in the meantime.
/// While the app is in the foreground the live stream rings instantly, and the
/// two paths coordinate through a "last seen id" so nothing rings twice.
class BackgroundNotify {
  static const taskName = 'school_notify_poll';
  static const _seenKey = 'school_notify_seen';
  static const _foregroundAtKey = 'school_notify_foreground_at';
  static const _serverKey = 'school_erp_server';
  static const _accessKey = 'school_erp_access';
  static const _defaultServer = 'https://school.wintechinov.com/api';

  static Future<void> register() async {
    try {
      final work = Workmanager();
      await work.initialize(backgroundDispatcher);
      await work.registerPeriodicTask(
        taskName,
        taskName,
        frequency: const Duration(minutes: 15),
        existingWorkPolicy: ExistingPeriodicWorkPolicy.keep,
        backoffPolicy: BackoffPolicy.linear,
        constraints: Constraints(networkType: NetworkType.connected),
      );
    } catch (_) {
      // Background polling is a fallback; the live stream still works.
    }
  }

  /// Marks the app as in the foreground so the background poll does not ring
  /// the same notification a second time. Refreshed while the feed runs.
  static Future<void> heartbeat() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setInt(_foregroundAtKey, DateTime.now().millisecondsSinceEpoch);
    } catch (_) {
      // Ignore; the timestamp simply stays stale.
    }
  }

  /// One background check: rings every unread notification newer than the last
  /// one the user has already been alerted about.
  static Future<void> pollOnce() async {
    final prefs = await SharedPreferences.getInstance();

    // The live stream is running, so it already rings new notifications.
    final foregroundAt = prefs.getInt(_foregroundAtKey) ?? 0;
    if (DateTime.now().millisecondsSinceEpoch - foregroundAt < 3 * 60 * 1000) {
      return;
    }

    final server = prefs.getString(_serverKey);
    final baseUrl = (server == null || server.isEmpty) ? _defaultServer : server;
    final token = await const FlutterSecureStorage().read(key: _accessKey);
    if (token == null) return; // Signed out.

    final response = await http
        .get(
          Uri.parse('$baseUrl/notifications?unreadOnly=true'),
          headers: {'Authorization': 'Bearer $token', 'Accept': 'application/json'},
        )
        .timeout(const Duration(seconds: 45));

    if (response.statusCode != 200) {
      // 401 means the session expired; the next app open renews it.
      return;
    }

    final decoded = jsonDecode(response.body);
    if (decoded is! List) return;

    var seen = prefs.getInt(_seenKey) ?? 0;
    final firstRun = !prefs.containsKey(_seenKey);
    var highest = seen;

    for (final row in decoded) {
      if (row is! Map) continue;
      final id = int.tryParse('${row['id']}') ?? 0;
      if (id > highest) highest = id;
      if (firstRun || id <= seen) continue;
      await Notify.show(
        '${row['title'] ?? 'School notification'}',
        '${row['message'] ?? ''}',
      );
    }

    await prefs.setInt(_seenKey, highest);
  }
}

@pragma('vm:entry-point')
void backgroundDispatcher() {
  Workmanager().executeTask((task, inputData) async {
    if (task != BackgroundNotify.taskName) return true;
    try {
      await BackgroundNotify.pollOnce();
    } catch (_) {
      // A failed poll must not fail the task.
    }
    return true;
  });
}
