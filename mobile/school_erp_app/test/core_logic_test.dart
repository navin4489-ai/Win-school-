import 'package:flutter_test/flutter_test.dart';
import 'package:school_erp_app/core/api_client.dart';
import 'package:school_erp_app/core/models.dart';

void main() {
  group('ApiClient.normalize', () {
    test('adds https and /api to a bare host', () {
      expect(ApiClient.normalize('school.wintechinov.com'),
          'https://school.wintechinov.com/api');
    });

    test('keeps an existing scheme', () {
      expect(ApiClient.normalize('http://10.0.0.5:5088'),
          'http://10.0.0.5:5088/api');
    });

    test('does not double up /api', () {
      expect(ApiClient.normalize('https://school.wintechinov.com/api'),
          'https://school.wintechinov.com/api');
    });

    test('strips trailing slashes before appending /api', () {
      expect(ApiClient.normalize('https://school.wintechinov.com///'),
          'https://school.wintechinov.com/api');
    });

    test('trims surrounding whitespace', () {
      expect(ApiClient.normalize('  example.com  '),
          'https://example.com/api');
    });
  });

  group('asInt / asIntOrNull', () {
    test('passes through an int', () => expect(asInt(7), 7));
    test('parses a numeric string', () => expect(asInt('42'), 42));
    test('truncates a double', () => expect(asInt(3.9), 3));
    test('bad input falls back to 0', () => expect(asInt('abc'), 0));
    test('null falls back to 0', () => expect(asInt(null), 0));
    test('asIntOrNull returns null on bad input',
        () => expect(asIntOrNull('x'), isNull));
    test('asIntOrNull parses a decimal string', () {
      expect(asIntOrNull('5.0'), 5);
    });
  });

  group('asDouble / asText', () {
    test('asDouble parses a string', () => expect(asDouble('1.5'), 1.5));
    test('asDouble bad input is 0', () => expect(asDouble('nope'), 0));
    test('asText stringifies a number', () => expect(asText(10), '10'));
    test('asText null is empty', () => expect(asText(null), ''));
  });

  group('NotificationItem.fromJson', () {
    test('parses a full server payload', () {
      final item = NotificationItem.fromJson({
        'id': 29,
        'title': 'Bell test',
        'message': 'Notification stream test',
        'category': 'Announcement',
        'channel': 0,
        'status': 0,
        'createdAtUtc': '2026-11-14T17:41:22.249Z',
        'readAtUtc': null,
      });
      expect(item.id, 29);
      expect(item.title, 'Bell test');
      expect(item.message, 'Notification stream test');
      expect(item.category, 'Announcement');
      expect(item.channel, '0');
      expect(item.status, '0');
      expect(item.isUnread, isTrue);
    });

    test('is read once readAtUtc is present', () {
      final item = NotificationItem.fromJson({
        'id': 1,
        'readAtUtc': '2026-11-14T18:00:00Z',
      });
      expect(item.isUnread, isFalse);
    });

    test('tolerates missing fields', () {
      final item = NotificationItem.fromJson({'id': 2});
      expect(item.id, 2);
      expect(item.title, '');
      expect(item.isUnread, isTrue);
    });
  });

  group('parseUtc', () {
    test('a timestamp without a zone is treated as UTC', () {
      // Regression: previously the whole string was replaced with "Z",
      // which parsed to null and dropped the time entirely.
      final dt = parseUtc('2026-11-14T12:00:00');
      expect(dt, isNotNull);
      expect(dt!.toUtc().hour, 12);
    });

    test('respects an explicit Z', () {
      final dt = parseUtc('2026-11-14T06:30:00Z');
      expect(dt!.toUtc().hour, 6);
      expect(dt.toUtc().minute, 30);
    });

    test('respects a numeric offset', () {
      final dt = parseUtc('2026-11-14T12:00:00+05:30');
      expect(dt!.toUtc().hour, 6);
      expect(dt.toUtc().minute, 30);
    });

    test('empty and null return null', () {
      expect(parseUtc(''), isNull);
      expect(parseUtc(null), isNull);
    });
  });
}
