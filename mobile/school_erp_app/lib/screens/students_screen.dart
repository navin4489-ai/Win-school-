import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';
import 'student_detail_screen.dart';

const _genericError = 'Something went wrong. Please try again.';

class StudentsScreen extends StatefulWidget {
  const StudentsScreen({super.key});

  @override
  State<StudentsScreen> createState() => _StudentsScreenState();
}

class _StudentsScreenState extends State<StudentsScreen> {
  final _search = TextEditingController();
  final List<StudentSummary> _students = [];
  bool _loading = true;
  bool _loadingMore = false;
  String? _error;
  int _page = 1;
  int _total = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({bool append = false}) async {
    if (append) {
      setState(() => _loadingMore = true);
    } else {
      setState(() {
        _loading = true;
        _error = null;
        _page = 1;
      });
    }

    try {
      final response = await context.read<AuthController>().api.get(
        'students',
        query: {
          'page': _page.toString(),
          'pageSize': '25',
          'status': 'Active',
          if (_search.text.trim().isNotEmpty) 'search': _search.text.trim(),
        },
      ) as Map<String, dynamic>;

      final items = (response['items'] as List<dynamic>? ?? [])
          .whereType<Map<String, dynamic>>()
          .map(StudentSummary.fromJson)
          .toList();

      if (!mounted) return;
      setState(() {
        if (!append) _students.clear();
        _students.addAll(items);
        _total = asInt(response['totalCount']);
        _loading = false;
        _loadingMore = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() => _error = exception.message);
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = _genericError);
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
          _loadingMore = false;
        });
      }
    }
  }

  Future<void> _openStudent(StudentSummary student) async {
    await Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => StudentDetailScreen(studentId: student.id)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Students')),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 6),
            child: TextField(
              controller: _search,
              textInputAction: TextInputAction.search,
              decoration: InputDecoration(
                hintText: 'Search name, admission no. or phone',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: _search.text.isNotEmpty
                    ? IconButton(
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _search.clear();
                          _load();
                        },
                      )
                    : null,
              ),
              onSubmitted: (_) => _load(),
            ),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18),
            child: Row(
              children: [
                Text(
                  '$_total active students',
                  style: const TextStyle(color: AppColors.muted, fontSize: 11),
                ),
                const Spacer(),
                TextButton(onPressed: () => _load(), child: const Text('Refresh')),
              ],
            ),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              child: Card(
                color: const Color(0xFFFDE8E6),
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(_error!, style: const TextStyle(color: AppColors.red, fontSize: 12)),
                ),
              ),
            ),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _students.isEmpty
                    ? const Center(
                        child: Text('No students found.', style: TextStyle(color: AppColors.muted)),
                      )
                    : RefreshIndicator(
                        onRefresh: () => _load(),
                        child: ListView.builder(
                          padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
                          itemCount: _students.length + 1,
                          itemBuilder: (context, index) {
                            if (index == _students.length) {
                              if (_students.length >= _total) return const SizedBox(height: 20);
                              return Padding(
                                padding: const EdgeInsets.only(top: 4),
                                child: OutlinedButton(
                                  onPressed: _loadingMore
                                      ? null
                                      : () {
                                          _page += 1;
                                          _load(append: true);
                                        },
                                  child: _loadingMore
                                      ? const SizedBox(
                                          width: 18,
                                          height: 18,
                                          child: CircularProgressIndicator(strokeWidth: 2),
                                        )
                                      : const Text('Load more'),
                                ),
                              );
                            }

                            final student = _students[index];
                            return Card(
                              margin: const EdgeInsets.only(bottom: 8),
                              child: ListTile(
                                onTap: () => _openStudent(student),
                                leading: CircleAvatar(
                                  radius: 19,
                                  backgroundColor: AppColors.tealSoft,
                                  child: Text(
                                    _initials(student.fullName),
                                    style: const TextStyle(
                                      color: AppColors.teal,
                                      fontWeight: FontWeight.w700,
                                      fontSize: 11,
                                    ),
                                  ),
                                ),
                                title: Text(
                                  student.fullName,
                                  style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
                                ),
                                subtitle: Text(
                                  '${student.admissionNo} · ${student.className} ${student.sectionName}',
                                  style: const TextStyle(fontSize: 10),
                                ),
                                trailing: const Icon(Icons.chevron_right, color: AppColors.muted),
                              ),
                            );
                          },
                        ),
                      ),
          ),
        ],
      ),
    );
  }

  String _initials(String name) => name
      .split(' ')
      .where((part) => part.isNotEmpty)
      .take(2)
      .map((part) => part[0].toUpperCase())
      .join();
}
