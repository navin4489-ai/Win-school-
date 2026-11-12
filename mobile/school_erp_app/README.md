# School ERP mobile app (Flutter)

Android and iOS app for parents, students, teachers and administrators. It covers the dashboard, students, attendance, fees, results and notifications.

## First-time setup

Only the Dart sources live in Git. Generate the platform folders once:

```bash
cd mobile/school_erp_app
flutter create . --org com.wintech --project-name school_erp_app
flutter pub get
```

`flutter create .` keeps the existing `lib/` and `pubspec.yaml`.

## Running

The API base URL is set at build time:

| Target | Command |
| --- | --- |
| Android emulator (default) | `flutter run` → `http://10.0.2.2:5080/api` |
| iOS simulator | `flutter run --dart-define=API_BASE_URL=http://localhost:5080/api` |
| Physical device | `flutter run --dart-define=API_BASE_URL=http://<your-LAN-IP>:5080/api` |
| Production | `flutter build appbundle --dart-define=API_BASE_URL=https://api.example.com/api` |

Plain HTTP for local development needs `android:usesCleartextTraffic="true"` in `android/app/src/main/AndroidManifest.xml` (debug only). For iOS you also need an ATS exception in `ios/Runner/Info.plist`.

## Access

Screens and actions follow the permissions returned at login. For example, the **Pay** button shows only with `fees.collect`, and attendance marking shows only with `attendance.mark`. Parents and students see only their own linked records.

Demo accounts (password `Passw0rd!`): `parent@demo.edu`, `student@demo.edu`, `teacher@demo.edu`, `principal@demo.edu`, `accounts@demo.edu`.
