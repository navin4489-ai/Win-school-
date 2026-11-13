import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';

class ResultsScreen extends StatefulWidget {
  const ResultsScreen({super.key});

  @override
  State<ResultsScreen> createState() => _ResultsScreenState();
}

class _ResultsScreenState extends State<ResultsScreen> {
  List<Exam> _exams = [];
  Exam? _selected;
  List<ExamResult> _results = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadExams();
  }

  Future<void> _loadExams() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final response = await context.read<AuthController>().api.get('exams') as List<dynamic>;
      final exams = response.whereType<Map<String, dynamic>>().map(Exam.fromJson).toList();
      if (!mounted) return;
      setState(() {
        _exams = exams;
        _selected = exams.isNotEmpty ? exams.first : null;
      });
      if (exams.isNotEmpty) {
        await _loadResults(exams.first);
      } else {
        setState(() => _loading = false);
      }
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _error = exception.message;
        _loading = false;
      });
    }
  }

  Future<void> _loadResults(Exam exam) async {
    setState(() {
      _loading = true;
      _error = null;
      _selected = exam;
    });

    try {
      final response =
          await context.read<AuthController>().api.get('exams/${exam.id}/results') as List<dynamic>;
      if (!mounted) return;
      setState(() {
        _results = response.whereType<Map<String, dynamic>>().map(ExamResult.fromJson).toList();
        _loading = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _results = [];
        _error = exception.message;
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final percent = NumberFormat('0.0');
    final date = DateFormat('dd MMM yyyy');

    return Scaffold(
      appBar: AppBar(title: const Text('Results')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 28),
        children: [
          if (_exams.isNotEmpty)
            DropdownButtonFormField<int>(
              initialValue: _selected?.id,
              decoration: const InputDecoration(labelText: 'Examination'),
              items: _exams
                  .map((exam) => DropdownMenuItem(value: exam.id, child: Text(exam.name)))
                  .toList(),
              onChanged: (value) {
                final matches = _exams.where((exam) => exam.id == value).toList();
                if (matches.isNotEmpty) _loadResults(matches.first);
              },
            ),
          if (_selected != null) ...[
            const SizedBox(height: 8),
            Text(
              '${_selected!.examType} · ${date.format(DateTime.parse(_selected!.startDate))} – '
              '${date.format(DateTime.parse(_selected!.endDate))}',
              style: const TextStyle(color: AppColors.muted, fontSize: 11),
            ),
          ],
          const SizedBox(height: 14),
          if (_error != null)
            Card(
              color: const Color(0xFFFDE8E6),
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Text(_error!, style: const TextStyle(color: AppColors.red)),
              ),
            ),
          if (_loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 40),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (_exams.isEmpty)
            const Card(
              child: Padding(
                padding: EdgeInsets.all(26),
                child: Center(
                  child: Text(
                    'No examinations have been configured.',
                    style: TextStyle(color: AppColors.muted),
                  ),
                ),
              ),
            )
          else if (_results.isEmpty)
            const Card(
              child: Padding(
                padding: EdgeInsets.all(26),
                child: Center(
                  child: Text(
                    'Results have not been published for this examination.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: AppColors.muted),
                  ),
                ),
              ),
            )
          else
            ..._results.map(
              (result) => Card(
                margin: const EdgeInsets.only(bottom: 9),
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Row(
                    children: [
                      CircleAvatar(
                        radius: 20,
                        backgroundColor:
                            result.resultStatus == 'Pass' ? AppColors.tealSoft : const Color(0xFFFDE8E6),
                        child: Text(
                          result.grade ?? '—',
                          style: TextStyle(
                            color: result.resultStatus == 'Pass' ? AppColors.teal : AppColors.red,
                            fontWeight: FontWeight.w700,
                            fontSize: 13,
                          ),
                        ),
                      ),
                      const SizedBox(width: 11),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              result.studentName,
                              style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
                            ),
                            Text(
                              '${result.admissionNo} · ${percent.format(result.percentage)}%',
                              style: const TextStyle(color: AppColors.muted, fontSize: 11),
                            ),
                          ],
                        ),
                      ),
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text(
                            '${percent.format(result.obtainedMarks)} / ${percent.format(result.totalMarks)}',
                            style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600),
                          ),
                          Text(
                            result.rank != null ? 'Rank ${result.rank}' : result.resultStatus,
                            style: const TextStyle(color: AppColors.muted, fontSize: 10),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
