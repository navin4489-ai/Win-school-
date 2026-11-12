import 'dart:convert';

import 'package:flutter/foundation.dart';
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

  final ApiClient api;

  UserProfile? user;
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
    api.accessToken = prefs.getString(_accessKey);
    api.refreshToken = prefs.getString(_refreshKey);

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
      await _clear();
      notifyListeners();
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

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_accessKey, access);
    await prefs.setString(_refreshKey, refresh);
    await prefs.setString(_userKey, jsonEncode(user!.toJson()));
  }

  Future<void> _clear() async {
    api.accessToken = null;
    api.refreshToken = null;
    user = null;

    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_accessKey);
    await prefs.remove(_refreshKey);
    await prefs.remove(_userKey);
  }
}
