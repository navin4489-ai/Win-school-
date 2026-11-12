import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

class ApiException implements Exception {
  ApiException(this.statusCode, this.message);

  final int statusCode;
  final String message;

  @override
  String toString() => message;
}

/// Thin JSON client for the School ERP API.
///
/// The base URL defaults to the Android emulator host alias. Override it with
/// `--dart-define=API_BASE_URL=https://api.example.com/api`.
class ApiClient {
  ApiClient({String? baseUrl, http.Client? client})
      : baseUrl = (baseUrl ??
                const String.fromEnvironment('API_BASE_URL',
                    defaultValue: 'http://10.0.2.2:5080/api'))
            .replaceAll(RegExp(r'/+$'), ''),
        _client = client ?? http.Client();

  final String baseUrl;
  final http.Client _client;

  String? accessToken;
  String? refreshToken;
  Future<bool> Function()? onRefresh;

  Future<dynamic> get(String path, {Map<String, String>? query}) =>
      _request('GET', path, query: query);

  Future<dynamic> post(String path, {Object? body}) =>
      _request('POST', path, body: body);

  Future<dynamic> put(String path, {Object? body}) =>
      _request('PUT', path, body: body);

  Future<dynamic> _request(
    String method,
    String path, {
    Map<String, String>? query,
    Object? body,
    bool allowRefresh = true,
  }) async {
    final uri = Uri.parse('$baseUrl/$path').replace(queryParameters: query);

    final request = http.Request(method, uri)
      ..headers['Accept'] = 'application/json';

    if (accessToken != null) {
      request.headers['Authorization'] = 'Bearer $accessToken';
    }
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }

    late http.Response response;
    try {
      final streamed = await _client.send(request).timeout(const Duration(seconds: 25));
      response = await http.Response.fromStream(streamed);
    } on TimeoutException {
      throw ApiException(0, 'The server took too long to respond.');
    } catch (_) {
      throw ApiException(0, 'Cannot reach the school server. Check your connection.');
    }

    if (response.statusCode == 401 &&
        allowRefresh &&
        onRefresh != null &&
        !path.startsWith('auth/')) {
      final refreshed = await onRefresh!.call();
      if (refreshed) {
        return _request(method, path, query: query, body: body, allowRefresh: false);
      }
    }

    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.body.isEmpty) return null;
      return jsonDecode(response.body);
    }

    throw ApiException(response.statusCode, _message(response));
  }

  String _message(http.Response response) {
    try {
      final decoded = jsonDecode(response.body);
      if (decoded is Map<String, dynamic>) {
        if (decoded['detail'] is String && (decoded['detail'] as String).isNotEmpty) {
          return decoded['detail'] as String;
        }
        if (decoded['title'] is String && (decoded['title'] as String).isNotEmpty) {
          return decoded['title'] as String;
        }
      }
    } catch (_) {
      // Fall through to a generic message.
    }

    if (response.statusCode == 403) return 'You do not have permission for this action.';
    if (response.statusCode == 404) return 'The requested record was not found.';
    if (response.statusCode >= 500) return 'The school server reported an error.';
    return 'Request failed (${response.statusCode}).';
  }
}
