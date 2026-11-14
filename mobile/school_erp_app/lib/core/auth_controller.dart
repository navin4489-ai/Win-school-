import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api_client.dart';
import 'models.dart';

class AuthController extends ChangeNotifier {
  AuthController({required this.api}) {
    api.onRefresh = _refreshSession;
  }

  static const _accessKey = 'school_erp_access';
  static const _refreshKey = 'school_erp_refresh';
  static const _userKey = 'school_erp_user';
  static const _serverKey = 'school_erp_server';
  static const _rawUserKey = 'school_erp_user_raw';

  static const _secure = FlutterSecureStorage();

  final ApiClient api;

  UserProfile? user;
  /// The user object exactly as the server sent it; the web portal reads this shape.
  String? rawUserJson;
  bool loading = true;
  bool busy = false;
  String? error;

  bool get isAuthenticated => user != null && api.accessToken != null;

  bool can(String permission) {
    final profile = user;
    if (profile == null) return false;
    return profile.roleCode == 'SuperAdmin' || profile.permissions.contains(permission);
  }

  Future<void> restore() async {
    final prefs = await SharedPreferences.getInstance();
    final server = prefs.getString(_serverKey);
    if (server != null && server.isNotEmpty) api.baseUrl = server;
    await _restoreTokens(prefs);
    rawUserJson = prefs.getString(_rawUserKey);

    final rawUser = prefs.getString(_userKey);
    if (rawUser != null) {
      try {
        user = UserProfile.fromJson(jsonDecode(rawUser) as Map<String, dynamic>);
      } catch (_) {
        await _clear();
      }
    }

    loading = false;
    notifyListeners();
  }

  Future<void> signIn({
    required String email,
    required String password,
    String schoolCode = '',
  }) async {
    busy = true;
    error = null;
    notifyListeners();

    try {
      final response = await api.post('auth/login', body: {
        'email': email.trim(),
        'password': password,
        if (schoolCode.trim().isNotEmpty) 'schoolCode': schoolCode.trim(),
      }) as Map<String, dynamic>;

      await _applySession(response);
    } on ApiException catch (exception) {
      error = exception.message;
      rethrow;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  String get serverUrl => api.baseUrl;

  /// Root of the web portal (the API lives under /api on the same host).
  String get webUrl {
    final base = api.baseUrl;
    return base.endsWith('/api') ? base.substring(0, base.length - 4) : base;
  }

  /// Renews the session so a freshly issued token can be handed to the web portal.
  Future<String?> freshAccessToken() async {
    final ok = await api.refreshOnce();
    return ok ? api.accessToken : null;
  }

  Future<void> setServerUrl(String value) async {
    if (value.trim().isEmpty) return;
    api.baseUrl = ApiClient.normalize(value);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_serverKey, api.baseUrl);
    notifyListeners();
  }

  Future<void> signOut() async {
    final refreshToken = api.refreshToken;
    if (refreshToken != null) {
      try {
        await api.post('auth/logout', body: {'refreshToken': refreshToken});
      } catch (_) {
        // Local sign-out must succeed even when the server is unreachable.
      }
    }
    await _clear();
    notifyListeners();
  }

  Future<bool> _refreshSession() async {
    final token = api.refreshToken;
    if (token == null) return false;

    try {
      final response = await api.post('auth/refresh', body: {'refreshToken': token});
      await _applySession(response as Map<String, dynamic>);
      return true;
    } catch (_) {
      final hadSession = user != null || api.accessToken != null;
      await _clear();
      if (hadSession) notifyListeners();
      return false;
    }
  }

  Future<void> _applySession(Map<String, dynamic> response) async {
    final access = response['accessToken']?.toString();
    final refresh = response['refreshToken']?.toString();
    final profile = response['user'];

    if (access == null || refresh == null || profile is! Map<String, dynamic>) {
      throw ApiException(0, 'The server returned an incomplete session.');
    }

    api.accessToken = access;
    api.refreshToken = refresh;
    user = UserProfile.fromJson(profile);
    rawUserJson = jsonEncode(profile);

    final prefs = await SharedPreferences.getInstance();
    await _secure.write(key: _accessKey, value: access);
    await _secure.write(key: _refreshKey, value: refresh);
    await prefs.setString(_userKey, jsonEncode(user!.toJson()));
    await prefs.setString(_rawUserKey, rawUserJson!);
  }

  Future<void> _clear() async {
    api.accessToken = null;
    api.refreshToken = null;
    user = null;
    rawUserJson = null;

    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_accessKey);
    await prefs.remove(_refreshKey);
    try {
      await _secure.delete(key: _accessKey);
      await _secure.delete(key: _refreshKey);
    } catch (_) {
      // Nothing more to clear if secure storage is unavailable.
    }
    await prefs.remove(_userKey);
    await prefs.remove(_rawUserKey);
  }

  Future<void> _restoreTokens(SharedPreferences prefs) async {
    final legacyAccess = prefs.getString(_accessKey);
    final legacyRefresh = prefs.getString(_refreshKey);
    try {
      if (legacyAccess != null || legacyRefresh != null) {
        if (legacyAccess != null) await _secure.write(key: _accessKey, value: legacyAccess);
        if (legacyRefresh != null) await _secure.write(key: _refreshKey, value: legacyRefresh);
        await prefs.remove(_accessKey);
        await prefs.remove(_refreshKey);
      }
      api.accessToken = await _secure.read(key: _accessKey);
      api.refreshToken = await _secure.read(key: _refreshKey);
    } catch (_) {
      api.accessToken = legacyAccess;
      api.refreshToken = legacyRefresh;
    }
  }
}