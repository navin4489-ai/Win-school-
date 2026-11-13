import 'dart:io';

import 'package:camera/camera.dart';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';

/// Staff attendance: check in and check out with a photo taken from the front
/// camera and the current location attached to the register entry.
class MyAttendanceScreen extends StatefulWidget {
  const MyAttendanceScreen({super.key});

  @override
  State<MyAttendanceScreen> createState() => _MyAttendanceScreenState();
}

class _MyAttendanceScreenState extends State<MyAttendanceScreen> {
  StaffRegister? _register;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  CameraController? _camera;
  bool _cameraReady = false;
  bool _capturing = false;

  Position? _position;
  String? _photoUrl;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _camera?.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final now = DateTime.now();
      final from = now.subtract(const Duration(days: 30));
      final response = await context.read<AuthController>().api.get(
        'attendance/staff/me',
        query: {
          'from': DateFormat('yyyy-MM-dd').format(from),
          'to': DateFormat('yyyy-MM-dd').format(now),
        },
      );
      if (!mounted) return;
      setState(() {
        _register = StaffRegister.fromJson(response as Map<String, dynamic>);
        _loading = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _error = exception.message;
        _loading = false;
      });
    }
  }

  // ---------------------------------------------------------------- camera

  Future<void> _startCamera() async {
    setState(() => _error = null);

    final cameras = await availableCameras();
    if (cameras.isEmpty) {
      setState(() => _error = 'No camera is available on this device.');
      return;
    }

    // Prefer the front camera so the teacher photographs themselves.
    final front = cameras.where((camera) => camera.lensDirection == CameraLensDirection.front).toList();
    final selected = front.isNotEmpty ? front.first : cameras.first;

    final controller = CameraController(
      selected,
      ResolutionPreset.medium,
      enableAudio: false,
      imageFormatGroup: ImageFormatGroup.jpeg,
    );

    try {
      await controller.initialize();
      if (!mounted) {
        await controller.dispose();
        return;
      }
      setState(() {
        _camera = controller;
        _cameraReady = true;
      });
    } on CameraException catch (exception) {
      setState(() => _error = 'The camera could not be started: ${exception.description ?? exception.code}');
    }
  }

  Future<void> _capturePhoto() async {
    final controller = _camera;
    if (controller == null || !controller.value.isTakingPicture) {
      setState(() => _error = 'The camera is not ready yet.');
      return;
    }

    final api = context.read<AuthController>().api;
    setState(() => _capturing = true);

    try {
      final shot = await controller.takePicture();
      final bytes = await File(shot.path).readAsBytes();

      final uploaded = await api.upload(
        'files/upload',
        fieldName: 'file',
        fileName: 'attendance-${DateTime.now().millisecondsSinceEpoch}.jpg',
        bytes: bytes,
        contentType: 'image/jpeg',
      ) as Map<String, dynamic>;

      if (!mounted) return;
      setState(() {
        _photoUrl = uploaded['url']?.toString();
        _capturing = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _capturing = false;
        _error = exception.message;
      });
    } on CameraException catch (exception) {
      if (!mounted) return;
      setState(() {
        _capturing = false;
        _error = 'The photo could not be taken: ${exception.description ?? exception.code}';
      });
    }
  }

  Future<void> _closeCamera() async {
    await _camera?.dispose();
    if (!mounted) return;
    setState(() {
      _camera = null;
      _cameraReady = false;
    });
  }

  // ------------------------------------------------------------- location

  Future<void> _captureLocation() async {
    setState(() {
      _error = null;
    });

    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        setState(() => _error = 'Turn on location services to record where you checked in.');
        return;
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        setState(() => _error = 'Location permission is required to record attendance.');
        return;
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
      );
      if (!mounted) return;
      setState(() => _position = position);
    } on Exception catch (exception) {
      if (!mounted) return;
      setState(() => _error = 'The location could not be determined: $exception');
    }
  }

  // -------------------------------------------------------------- submit

  Future<void> _submit(String action) async {
    if (_busy) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await context.read<AuthController>().api.post(
        'attendance/staff/$action',
        body: {
          'photoUrl': _photoUrl,
          'latitude': _position?.latitude,
          'longitude': _position?.longitude,
          'source': 'Mobile',
        },
      );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(action == 'check-in' ? 'Checked in.' : 'Checked out.'),
        ),
      );
      setState(() {
        _photoUrl = null;
        _busy = false;
      });
      await _load();
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = exception.message;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final money = NumberFormat('0.0');
    final today = _register?.records.where((r) => r.date == DateFormat('yyyy-MM-dd').format(DateTime.now())).firstOrNull;

    return Scaffold(
      appBar: AppBar(title: const Text('My attendance')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 28),
        children: [
          if (_error != null)
            Card(
              color: const Color(0xFFFDE8E6),
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Text(_error!, style: const TextStyle(color: AppColors.red, fontSize: 12)),
              ),
            ),
          if (_loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 40),
              child: Center(child: CircularProgressIndicator()),
            ),
          if (!_loading) ...[
            if (_register != null) ...[
            Row(
              children: [
                Expanded(child: _Stat(label: 'Present', value: '${_register!.present}', color: AppColors.green)),
                const SizedBox(width: 8),
                Expanded(child: _Stat(label: 'Late', value: '${_register!.late}', color: AppColors.amber)),
                const SizedBox(width: 8),
                Expanded(child: _Stat(label: 'Absent', value: '${_register!.absent}', color: AppColors.red)),
                const SizedBox(width: 8),
                Expanded(child: _Stat(label: 'Hours', value: money.format(_register!.hoursWorked), color: AppColors.teal)),
              ],
            ),
            const SizedBox(height: 14),
          ],
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    'Today',
                    style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    today == null
                        ? 'No entry recorded yet.'
                        : 'In ${_time(today.checkInUtc)} · Out ${today.hasCheckOut ? _time(today.checkOutUtc) : '—'}'
                            ' · ${today.status}',
                    style: const TextStyle(color: AppColors.muted, fontSize: 12),
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: _busy ? null : () => _startCamera(),
                          icon: const Icon(Icons.photo_camera_outlined, size: 16),
                          label: Text(_cameraReady ? 'Camera on' : 'Take photo'),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: _busy || _capturing ? null : _capturePhoto,
                          icon: _capturing
                              ? const SizedBox(
                                  width: 14,
                                  height: 14,
                                  child: CircularProgressIndicator(strokeWidth: 2),
                                )
                              : const Icon(Icons.camera_alt_outlined, size: 16),
                          label: const Text('Capture'),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: _busy ? null : _captureLocation,
                          icon: const Icon(Icons.my_location, size: 16),
                          label: Text(_position == null ? 'Get location' : 'Location ok'),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: _busy || !_cameraReady ? null : _closeCamera,
                          icon: const Icon(Icons.close, size: 16),
                          label: const Text('Close camera'),
                        ),
                      ),
                    ],
                  ),
                  if (_position != null) ...[
                    const SizedBox(height: 10),
                    Text(
                      'Recorded at ${_position!.latitude.toStringAsFixed(5)}, '
                      '${_position!.longitude.toStringAsFixed(5)}',
                      style: const TextStyle(color: AppColors.muted, fontSize: 11),
                    ),
                  ],
                  if (_photoUrl != null) ...[
                    const SizedBox(height: 10),
                    const Row(
                      children: [
                        Icon(Icons.check_circle, color: AppColors.green, size: 16),
                        SizedBox(width: 6),
                        Text(
                          'Photo attached',
                          style: TextStyle(color: AppColors.green, fontSize: 12),
                        ),
                      ],
                    ),
                  ],
                  const SizedBox(height: 14),
                  Row(
                    children: [
                      Expanded(
                        child: FilledButton(
                          onPressed: _busy ? null : () => _submit('check-in'),
                          child: const Text('Check in'),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: OutlinedButton(
                          onPressed: _busy ? null : () => _submit('check-out'),
                          child: const Text('Check out'),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 18),
          const Text('Register', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
          const SizedBox(height: 8),
          if (_register != null && _register!.records.isEmpty)
            const Card(
              child: Padding(
                padding: EdgeInsets.all(20),
                child: Center(
                  child: Text('No attendance recorded in the last 30 days.',
                      style: TextStyle(color: AppColors.muted)),
                ),
              ),
            )
          else if (_register != null)
            ..._register!.records.reversed.map(
                  (record) => Card(
                    margin: const EdgeInsets.only(bottom: 8),
                    child: ListTile(
                      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 2),
                      leading: Icon(
                        record.status == 'Absent'
                            ? Icons.error_outline
                            : record.status == 'Late'
                                ? Icons.schedule
                                : Icons.check_circle_outline,
                        color: record.status == 'Absent'
                            ? AppColors.red
                            : record.status == 'Late'
                                ? AppColors.amber
                                : AppColors.green,
                      ),
                      title: Text(
                        DateFormat('dd MMM yyyy').format(DateTime.parse(record.date)),
                        style: const TextStyle(fontSize: 13),
                      ),
                      subtitle: Text(
                        'In ${_time(record.checkInUtc)} · Out ${_time(record.checkOutUtc)}',
                        style: const TextStyle(fontSize: 10),
                      ),
                      trailing: record.checkInPhotoUrl == null
                          ? null
                          : const Icon(Icons.photo_camera_back_outlined, size: 16, color: AppColors.teal),
                    ),
                  ),
                ),
          ],
        ],
      ),
    );
  }

  String _time(DateTime? value) =>
      value == null ? '—' : DateFormat('HH:mm').format(value);
}

class _Stat extends StatelessWidget {
  const _Stat({required this.label, required this.value, required this.color});

  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 10),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        children: [
          Text(
            value,
            style: TextStyle(color: color, fontSize: 17, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 2),
          Text(
            label.toUpperCase(),
            style: const TextStyle(
              color: AppColors.muted,
              fontSize: 8,
              letterSpacing: 0.5,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}
