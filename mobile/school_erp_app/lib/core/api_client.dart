import 'dart:async';
import 'dart:convert';
import 'dart:io';

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
/// The base URL defaults to the production API. Override it with
/// `--dart-define=API_BASE_URL=https://api.example.com/api`.
class ApiClient {
  ApiClient({String? baseUrl, http.Client? client})
      : baseUrl = normalize(baseUrl ?? defaultBaseUrl),
        _client = client ?? http.Client();

  static const defaultBaseUrl = String.fromEnvironment('API_BASE_URL',
      defaultValue: 'https://school.wintechinov.com/api');

  static const _normalTimeout = Duration(seconds: 30);
  static const _transferTimeout = Duration(seconds: 120);
  static const _offlineMessage =
      'Cannot reach the server. Check your internet connection.';
  static const _expiredMessage = 'Your session has expired. Please sign in again.';

  /// Accepts `host:port`, `https://host` or a full `.../api` URL and returns the API root.
  static String normalize(String value) {
    var url = value.trim().replaceAll(RegExp(r'/+$'), '');
    if (!url.startsWith('http://') && !url.startsWith('https://')) url = 'https://$url';
    if (!url.endsWith('/api')) url = '$url/api';
    return url;
  }

  String baseUrl;
  final http.Client _client;

  String? accessToken;
  String? refreshToken;
  Future<bool> Function()? onRefresh;

  Future<bool>? _refreshing;

  /// Runs the refresh callback, sharing a single in-flight refresh between callers.
  Future<bool> refreshOnce() {
    final callback = onRefresh;
    if (callback == null) return Future.value(false);
    final existing = _refreshing;
    if (existing != null) return existing;
    final future = () async {
      try {
        return await callback();
      } catch (_) {
        return false;
      } finally {
        _refreshing = null;
      }
    }();
    _refreshing = future;
    return future;
  }

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
    http.BaseRequest build() {
      final request = http.MultipartRequest('POST', Uri.parse('$baseUrl/$path'))
        ..headers['Accept'] = 'application/json';
      _authorize(request);
      request.files.add(
        http.MultipartFile.fromBytes(
          fieldName,
          bytes,
          filename: fileName,
          contentType: MediaType.parse(contentType),
        ),
      );
      return request;
    }

    final response = await _sendWithRefresh(path, build, _transferTimeout);
    return _decode(response);
  }

  /// Downloads a binary document such as a receipt or a fee statement.
  Future<List<int>> download(String path, {Map<String, String>? query}) async {
    final uri = Uri.parse('$baseUrl/$path').replace(queryParameters: query);
    http.BaseRequest build() {
      final request = http.Request('GET', uri)
        ..headers['Accept'] = 'application/pdf, application/octet-stream, */*';
      _authorize(request);
      return request;
    }

    final response = await _sendWithRefresh(path, build, _transferTimeout);
    if (response.statusCode >= 200 && response.statusCode < 300) return response.bodyBytes;
    throw ApiException(response.statusCode, _message(response));
  }

  Future<dynamic> _request(
    String method,
    String path, {
    Map<String, String>? query,
    Object? body,
  }) async {
    final uri = Uri.parse('$baseUrl/$path').replace(queryParameters: query);

    http.BaseRequest build() {
      final request = http.Request(method, uri)..headers['Accept'] = 'application/json';
      _authorize(request);
      if (body != null) {
        request.headers['Content-Type'] = 'application/json';
        request.body = jsonEncode(body);
      }
      return request;
    }

    final response = await _sendWithRefresh(path, build, _normalTimeout);
    return _decode(response);
  }

  void _authorize(http.BaseRequest request) {
    final token = accessToken;
    if (token != null) request.headers['Authorization'] = 'Bearer $token';
  }

  Future<http.Response> _sendWithRefresh(
    String path,
    http.BaseRequest Function() build,
    Duration timeout,
  ) async {
    final response = await _send(build(), timeout);
    if (response.statusCode != 401 || onRefresh == null || path.startsWith('auth/')) {
      return response;
    }

    final refreshed = await refreshOnce();
    if (!refreshed) throw ApiException(401, _expiredMessage);

    final retry = await _send(build(), timeout);
    if (retry.statusCode == 401) throw ApiException(401, _expiredMessage);
    return retry;
  }

  Future<http.Response> _send(http.BaseRequest request, Duration timeout) async {
    try {
      final streamed = await _client.send(request).timeout(timeout);
      return await http.Response.fromStream(streamed).timeout(timeout);
    } on SocketException {
      throw ApiException(0, _offlineMessage);
    } on TimeoutException {
      throw ApiException(0, _offlineMessage);
    } on HttpException {
      throw ApiException(0, _offlineMessage);
    } on http.ClientException {
      throw ApiException(0, _offlineMessage);
    } on HandshakeException {
      throw ApiException(0, _offlineMessage);
    }
  }

  dynamic _decode(http.Response response) {
    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.body.trim().isEmpty) return null;
      try {
        return jsonDecode(response.body);
      } on FormatException {
        throw ApiException(0, 'Unexpected response from the server.');
      }
    }
    throw ApiException(response.statusCode, _message(response));
  }

  String _message(http.Response response) {
    final status = response.statusCode;
    if (status == 429) return 'Too many attempts. Please wait a minute and try again.';
    if (status == 403) return 'You do not have permission to do this.';

    String body;
    try {
      body = response.body;
    } catch (_) {
      body = '';
    }

    dynamic decoded;
    try {
      decoded = body.trim().isEmpty ? null : jsonDecode(body);
    } catch (_) {
      decoded = null;
    }

    if (decoded is Map<String, dynamic>) {
      final detail = decoded['detail'];
      if (detail is String && detail.trim().isNotEmpty) return detail;
      final errors = decoded['errors'];
      if (errors is Map) {
        final parts = <String>[];
        for (final value in errors.values) {
          if (value is List) {
            parts.addAll(value.map((e) => e.toString()));
          } else if (value != null) {
            parts.add(value.toString());
          }
        }
        if (parts.isNotEmpty) return parts.join('\n');
      }
      final title = decoded['title'];
      if (title is String && title.trim().isNotEmpty) return title;
    } else if (decoded is String && decoded.trim().isNotEmpty) {
      return decoded;
    } else if (decoded == null && body.trim().isNotEmpty &&
        !body.trimLeft().startsWith('<')) {
      final text = body.trim();
      return text.length > 200 ? text.substring(0, 200) : text;
    }

    if (status == 401) return _expiredMessage;
    if (status == 404) return 'The requested record was not found.';
    if (status >= 500) return 'The school server reported an error.';
    return 'Request failed ($status).';
  }
}
