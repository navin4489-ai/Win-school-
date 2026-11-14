import 'dart:ui' show Color;

import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:permission_handler/permission_handler.dart';

/// Local notifications with sound: the school bell that rings when a new
/// notification arrives, whether the app is open or in the background.
class Notify {
  static const channelId = 'school_erp';
  static const channelName = 'School notifications';

  static final FlutterLocalNotificationsPlugin _plugin =
      FlutterLocalNotificationsPlugin();
  static bool _initialised = false;
  static bool _permissionAsked = false;

  static Future<void> init() async {
    if (_initialised) return;
    _initialised = true;

    const settings = InitializationSettings(
      android: AndroidInitializationSettings('@mipmap/ic_launcher'),
      iOS: DarwinInitializationSettings(
        requestAlertPermission: false,
        requestBadgePermission: false,
        requestSoundPermission: false,
      ),
    );

    try {
      await _plugin.initialize(settings: settings);
    } catch (_) {
      // The channel is created again on the next notification.
    }

    final android =
        _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
    await android?.createNotificationChannel(const AndroidNotificationChannel(
      channelId,
      channelName,
      description: 'Attendance, fees, announcements and other school alerts',
      importance: Importance.max,
      playSound: true,
      enableVibration: true,
      enableLights: true,
    ));
  }

  /// Asks Android 13+ / iOS for permission to show notifications.
  static Future<bool> requestPermission() async {
    await init();
    if (_permissionAsked) return true;
    _permissionAsked = true;
    try {
      final status = await Permission.notification.status;
      if (status.isGranted) return true;
      final result = await Permission.notification.request();
      return result.isGranted;
    } catch (_) {
      return false;
    }
  }

  /// Shows a notification that rings and vibrates.
  static Future<void> show(String title, String body) async {
    await init();
    try {
      await _plugin.show(
        id: DateTime.now().millisecondsSinceEpoch ~/ 1000,
        title: title,
        body: body,
        notificationDetails: NotificationDetails(
          android: AndroidNotificationDetails(
            channelId,
            channelName,
            channelDescription:
                'Attendance, fees, announcements and other school alerts',
            importance: Importance.max,
            priority: Priority.high,
            playSound: true,
            enableVibration: true,
            enableLights: true,
            color: const Color(0xFF12345C),
            styleInformation: BigTextStyleInformation(body),
          ),
          iOS: const DarwinNotificationDetails(
            presentAlert: true,
            presentBadge: true,
            presentSound: true,
          ),
        ),
      );
    } catch (_) {
      // Never let a missing notification channel break the screen.
    }
  }
}
