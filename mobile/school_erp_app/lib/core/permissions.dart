import 'package:permission_handler/permission_handler.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Asks for the permissions the app needs (camera for check-in photos, location for
/// check-in position) once, on the first launch. Later uses still ask again if needed.
class AppPermissions {
  static const _askedKey = 'permissions_requested_v1';

  static Future<void> requestOnFirstRun() async {
    final prefs = await SharedPreferences.getInstance();
    if (prefs.getBool(_askedKey) ?? false) return;
    await prefs.setBool(_askedKey, true);

    try {
      await [Permission.camera, Permission.locationWhenInUse].request();
    } catch (_) {
      // Permission prompts are best effort; each feature asks again when used.
    }
  }
}
