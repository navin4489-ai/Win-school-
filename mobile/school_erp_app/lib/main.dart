import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/api_client.dart';
import 'core/auth_controller.dart';
import 'core/background_notify.dart';
import 'core/notify.dart';
import 'core/permissions.dart';
import 'core/theme.dart';
import 'screens/home_screen.dart';
import 'screens/login_screen.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final auth = AuthController(api: ApiClient());
  await auth.restore();

  // Notification channel (sound + badge) and the background poll that rings
  // when the app is closed.
  await Notify.init();
  await BackgroundNotify.register();

  runApp(
    ChangeNotifierProvider<AuthController>.value(
      value: auth,
      child: const SchoolErpApp(),
    ),
  );

  // First launch: ask for camera and location (used for staff check-in).
  WidgetsBinding.instance.addPostFrameCallback((_) => AppPermissions.requestOnFirstRun());
}

class SchoolErpApp extends StatelessWidget {
  const SchoolErpApp({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthController>();

    return MaterialApp(
      title: 'Wintech School ERP',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(),
      home: auth.loading
          ? const _SplashScreen()
          : auth.isAuthenticated
              ? const HomeScreen()
              : const LoginScreen(),
    );
  }
}

class _SplashScreen extends StatelessWidget {
  const _SplashScreen();

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      backgroundColor: AppColors.navy,
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ClipRRect(
              borderRadius: BorderRadius.all(Radius.circular(16)),
              child: Image(image: AssetImage('assets/logo.png'), width: 64, height: 64),
            ),
            SizedBox(height: 16),
            Text(
              'Wintech School ERP',
              style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w600),
            ),
            SizedBox(height: 18),
            SizedBox(
              width: 22,
              height: 22,
              child: CircularProgressIndicator(strokeWidth: 2, color: AppColors.gold),
            ),
          ],
        ),
      ),
    );
  }
}
