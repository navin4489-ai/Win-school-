import 'package:permission_handler/permission_handler.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Asks for the permissions the app needs once, on the first launch: camera
/// and location for staff check-in, notifications so the bell can ring with a
/// sound. Later uses still ask again if needed.
class AppPermissions {
  static const _askedKey = 'permissions_requested_v1';
  static const _notificationsKey = 'notifications_requested_v1';

  static Future<void> requestOnFirstRun() async {
    final prefs = await SharedPreferences.getInstance();

    // Existing installs already asked for camera and location; they still need
    // to be asked for notifications once.
    final askDevice = !(prefs.getBool(_askedKey) ?? false);
    final askNotifications = !(prefs.getBool(_notificationsKey) ?? false);
    if (!askDevice && !askNotifications) return;

    if (askDevice) await prefs.setBool(_askedKey, true);
    if (askNotifications) await prefs.setBool(_notificationsKey, true);

    try {
      final permissions = <Permission>[
        if (askDevice) ...[Permission.camera, Permission.locationWhenInUse],
        if (askNotifications) Permission.notification,
      ];
      if (permissions.isNotEmpty) await permissions.request();
    } catch (_) {
      // Permission prompts are best effort; each feature asks again when used.
    }
  }
}
