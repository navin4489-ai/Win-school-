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

## Camera and location permissions

The **My attendance** screen records a photo from the front camera and the GPS position with
every check-in and check-out. Both permissions have to be declared in the generated
platform folders, otherwise the device denies the request silently.

`android/app/src/main/AndroidManifest.xml` — add inside `<manifest>`:

```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
<uses-feature android:name="android.hardware.camera.front" android:required="false" />
```

`ios/Runner/Info.plist` — add inside `<dict>`:

```xml
<key>NSCameraUsageDescription</key>
<string>Your photo is attached to your attendance record so the register can be verified.</string>
<key>NSLocationWhenInUseUsageDescription</key>
<string>Your location is recorded with check-in and check-out.</string>
```

## Access

Screens and actions follow the permissions returned at login. For example, the **Pay** button shows only with `fees.collect`, and attendance marking shows only with `attendance.mark`. Parents and students see only their own linked records.

| Screen | Who sees it | What it does |
| --- | --- | --- |
| Dashboard | Everyone | Role-specific cards and recent activity |
| My attendance | `attendance.mark` | Check in / out with a front-camera photo and GPS |
| Attendance | `attendance.read` | Mark the class roster |
| Fees | `fees.read` | Ledger, late fee, online payment, receipt and statement download |
| Results | `exams.read` | Exam results and report cards |
| Students | `students.read` | Student directory and profile |
| Notifications | Everyone | In-app alerts and broadcast messages |

A parent works from their own children: the fee screen lists them, takes an online payment
through the gateway and hands the receipt or the full statement to the platform share
sheet so it can be saved or printed.

Demo accounts (password `Passw0rd!`): `parent@demo.edu`, `student@demo.edu`, `teacher@demo.edu`, `principal@demo.edu`, `accounts@demo.edu`.
