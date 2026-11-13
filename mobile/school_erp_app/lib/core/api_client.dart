import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

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
      : baseUrl = normalize(baseUrl ?? defaultBaseUrl),
        _client = client ?? http.Client();

  static const defaultBaseUrl = String.fromEnvironment('API_BASE_URL',
      defaultValue: 'http://10.0.2.2:5080/api');

  /// Accepts `host:port`, `https://host` or a full `.../api` URL and returns the API root.
  static String normalize(String value) {
    var url = value.trim().replaceAll(RegExp(r'/+$'), '');
    if (!url.startsWith('http://') && !url.startsWith('https://')) url = 'http://$url';
    if (!url.endsWith('/api')) url = '$url/api';
    return url;
  }

  String baseUrl;
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

  /// Uploads a single file and returns the decoded JSON response.
  Future<dynamic> upload(
    String path, {
    required String fieldName,
    required String fileName,
    required List<int> bytes,
    String contentType = 'application/octet-stream',
  }) async {
    final request = http.MultipartRequest('POST', Uri.parse('$baseUrl/$path'));
    if (accessToken != null) {
      request.headers['Authorization'] = 'Bearer $accessToken';
    }
    request.files.add(
      http.MultipartFile.fromBytes(
        fieldName,
        bytes,
        filename: fileName,
        contentType: MediaType.parse(contentType),
      ),
    );

    final streamed = await _client.send(request).timeout(const Duration(seconds: 45));
    final response = await http.Response.fromStream(streamed);

    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.body.isEmpty) return null;
      return jsonDecode(response.body);
    }

    throw ApiException(response.statusCode, _message(response));
  }

  /// Downloads a binary document such as a receipt or a fee statement.
  Future<List<int>> download(String path, {Map<String, String>? query}) async {
    final uri = Uri.parse('$baseUrl/$path').replace(queryParameters: query);
    final request = http.Request('GET', uri);
    if (accessToken != null) {
      request.headers['Authorization'] = 'Bearer $accessToken';
    }

    final streamed = await _client.send(request).timeout(const Duration(seconds: 45));
    final response = await http.Response.fromStream(streamed);

    if (response.statusCode >= 200 && response.statusCode < 300) return response.bodyBytes;

    throw ApiException(response.statusCode, _message(response));
  }

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
