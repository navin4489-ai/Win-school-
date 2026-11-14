import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';

const _genericError = 'Something went wrong. Please try again.';

class AttendanceScreen extends StatefulWidget {
  const AttendanceScreen({super.key});

  @override
  State<AttendanceScreen> createState() => _AttendanceScreenState();
}

class _AttendanceScreenState extends State<AttendanceScreen> {
  final _statuses = const ['Present', 'Absent', 'Late', 'HalfDay', 'Leave'];

  List<ClassRoom> _classes = [];
  List<Section> _sections = [];
  ClassRoom? _selectedClass;
  Section? _selectedSection;
  DateTime _date = DateTime.now();
  ClassAttendance? _attendance;
  bool _loading = true;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadClasses();
  }

  Future<void> _loadClasses() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final response = await context.read<AuthController>().api.get('admin/classes') as List<dynamic>;
      final classes = response.whereType<Map<String, dynamic>>().map(ClassRoom.fromJson).toList();
      if (!mounted) return;
      setState(() {
        _classes = classes;
        _loading = false;
      });
      if (classes.isNotEmpty) {
        await _selectClass(classes.first);
      }
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _error = exception.message;
        _loading = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = _genericError);
    } finally {
      if (mounted && _loading) setState(() => _loading = false);
    }
  }

  Future<void> _selectClass(ClassRoom room) async {
    setState(() {
      _selectedClass = room;
      _sections = [];
      _selectedSection = null;
      _attendance = null;
    });

    try {
      final response = await context.read<AuthController>().api
          .get('admin/classes/${room.id}/sections') as List<dynamic>;
      final sections = response.whereType<Map<String, dynamic>>().map(Section.fromJson).toList();
      if (!mounted) return;
      setState(() => _sections = sections);
      if (sections.isNotEmpty) {
        await _selectSection(sections.first);
      }
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() => _error = exception.message);
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = _genericError);
    }
  }

  Future<void> _selectSection(Section section) async {
    setState(() {
      _selectedSection = section;
      _attendance = null;
    });
    await _loadRoster();
  }

  Future<void> _loadRoster() async {
    final room = _selectedClass;
    final section = _selectedSection;
    if (room == null || section == null) return;

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final response = await context.read<AuthController>().api.get(
        'attendance/roster',
        query: {
          'classId': room.id.toString(),
          'sectionId': section.id.toString(),
          'date': _iso(_date),
        },
      );
      final roster = ClassAttendance.fromJson(response as Map<String, dynamic>);
      for (final student in roster.students) {
        student.status ??= 'Present';
      }
      if (!mounted) return;
      setState(() {
        _attendance = roster;
        _loading = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _error = exception.message;
        _loading = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = _genericError);
    } finally {
      if (mounted && _loading) setState(() => _loading = false);
    }
  }

  Future<void> _pickDate() async {
    final selected = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(2020),
      lastDate: DateTime.now().add(const Duration(days: 365)),
    );
    if (selected == null) return;
    setState(() => _date = selected);
    await _loadRoster();
  }

  Future<void> _save() async {
    final roster = _attendance;
    if (roster == null || _saving) return;

    final entries = roster.students
        .where((student) => student.status != null)
        .map((student) => {
              'studentId': student.studentId,
              'status': student.status,
            })
        .toList();

    if (entries.isEmpty) {
      _showError('Mark at least one student.');
      return;
    }

    setState(() => _saving = true);
    try {
      final result = await context.read<AuthController>().api.post('attendance/mark', body: {
        'classId': roster.classId,
        'sectionId': roster.sectionId,
        'date': roster.date,
        'entries': entries,
      }) as Map<String, dynamic>;

      if (!mounted) return;
      setState(() => _saving = false);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            '${result['created'] ?? 0} created, ${result['updated'] ?? 0} updated, '
            '${result['notificationsQueued'] ?? 0} parent alerts queued.',
          ),
        ),
      );
      await _loadRoster();
    } on ApiException catch (exception) {
      if (!mounted) return;
      _showError(exception.message);
    } catch (_) {
      if (!mounted) return;
      _showError(_genericError);
    } finally {
      if (mounted && _saving) setState(() => _saving = false);
    }
  }

  void _markAllPresent() {
    final roster = _attendance;
    if (roster == null) return;
    setState(() {
      for (final student in roster.students) {
        student.status = 'Present';
      }
    });
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), backgroundColor: AppColors.red),
    );
  }

  @override
  Widget build(BuildContext context) {
    final canMark = context.read<AuthController>().can('attendance.mark');
    final present = _count('Present');
    final absent = _count('Absent');

    return Scaffold(
      appBar: AppBar(
        title: const Text('Attendance'),
        actions: [
          if (_attendance != null && canMark)
            TextButton(
              onPressed: _markAllPresent,
              child: const Text('All present'),
            ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 28),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                children: [
                  DropdownButtonFormField<int>(
                    initialValue: _selectedClass?.id,
                    decoration: const InputDecoration(labelText: 'Class'),
                    items: _classes
                        .map((room) => DropdownMenuItem(value: room.id, child: Text(room.name)))
                        .toList(),
                    onChanged: (value) {
                      final room = _classes.where((item) => item.id == value).toList();
                      if (room.isNotEmpty) _selectClass(room.first);
                    },
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<int>(
                    initialValue: _selectedSection?.id,
                    decoration: const InputDecoration(labelText: 'Section'),
                    items: _sections
                        .map((section) => DropdownMenuItem(value: section.id, child: Text(section.name)))
                        .toList(),
                    onChanged: (value) {
                      final section = _sections.where((item) => item.id == value).toList();
                      if (section.isNotEmpty) _selectSection(section.first);
                    },
                  ),
                  const SizedBox(height: 10),
                  OutlinedButton.icon(
                    onPressed: _pickDate,
                    icon: const Icon(Icons.calendar_today_outlined, size: 16),
                    label: Text(DateFormat('EEE, dd MMM yyyy').format(_date)),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),
          if (_error != null)
            Card(
              color: const Color(0xFFFDE8E6),
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Text(_error!, style: const TextStyle(color: AppColors.red)),
              ),
            ),
          if (_attendance != null) ...[
            Row(
              children: [
                _CountBadge(label: 'Present', count: present, color: AppColors.green),
                const SizedBox(width: 8),
                _CountBadge(label: 'Absent', count: absent, color: AppColors.red),
                const Spacer(),
                Text(
                  '${_attendance!.students.length} students',
                  style: const TextStyle(color: AppColors.muted, fontSize: 12),
                ),
              ],
            ),
            const SizedBox(height: 10),
          ],
          if (_loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 40),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (_attendance == null)
            const Card(
              child: Padding(
                padding: EdgeInsets.all(26),
                child: Center(
                  child: Text(
                    'Choose a class and section to load the roster.',
                    style: TextStyle(color: AppColors.muted),
                  ),
                ),
              ),
            )
          else ...[
            ..._attendance!.students.map(
              (student) => Card(
                margin: const EdgeInsets.only(bottom: 8),
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(13, 11, 13, 11),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          CircleAvatar(
                            radius: 16,
                            backgroundColor: AppColors.tealSoft,
                            child: Text(
                              _initials(student.fullName),
                              style: const TextStyle(
                                color: AppColors.teal,
                                fontSize: 10,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                          const SizedBox(width: 9),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  student.fullName,
                                  style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
                                ),
                                Text(
                                  '${student.admissionNo} · Roll ${student.rollNo ?? '—'}',
                                  style: const TextStyle(color: AppColors.muted, fontSize: 10),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 6,
                        runSpacing: 5,
                        children: _statuses
                            .map(
                              (status) => ChoiceChip(
                                label: Text(status, style: const TextStyle(fontSize: 10)),
                                selected: student.status == status,
                                onSelected: canMark
                                    ? (_) => setState(() => student.status = status)
                                    : null,
                                visualDensity: VisualDensity.compact,
                              ),
                            )
                            .toList(),
                      ),
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 12),
            if (canMark)
              FilledButton(
                onPressed: _saving ? null : _save,
                child: _saving
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                      )
                    : const Text('Submit attendance'),
              ),
          ],
        ],
      ),
    );
  }

  int _count(String status) =>
      _attendance?.students.where((student) => student.status == status).length ?? 0;

  String _initials(String name) => name
      .split(' ')
      .where((part) => part.isNotEmpty)
      .take(2)
      .map((part) => part[0].toUpperCase())
      .join();

  String _iso(DateTime date) => DateFormat('yyyy-MM-dd').format(date);
}

class _CountBadge extends StatelessWidget {
  const _CountBadge({required this.label, required this.count, required this.color});

  final String label;
  final int count;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Text(
        '$count $label',
        style: TextStyle(color: color, fontSize: 10, fontWeight: FontWeight.w700),
      ),
    );
  }
}
