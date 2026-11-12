import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';

class StudentDetailScreen extends StatefulWidget {
  const StudentDetailScreen({super.key, required this.studentId});

  final int studentId;

  @override
  State<StudentDetailScreen> createState() => _StudentDetailScreenState();
}

class _StudentDetailScreenState extends State<StudentDetailScreen> {
  StudentDetail? _student;
  Ledger? _ledger;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final api = context.read<AuthController>().api;
      final responses = await Future.wait([
        api.get('students/${widget.studentId}'),
        api.get('fees/students/${widget.studentId}/ledger').catchError((_) => null),
      ]);

      if (!mounted) return;
      setState(() {
        _student = StudentDetail.fromJson(responses[0] as Map<String, dynamic>);
        final ledger = responses[1];
        _ledger = ledger is Map<String, dynamic> ? Ledger.fromJson(ledger) : null;
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

  @override
  Widget build(BuildContext context) {
    final money = NumberFormat.currency(locale: 'en_IN', symbol: '₹', decimalDigits: 0);
    final date = DateFormat('dd MMM yyyy');

    return Scaffold(
      appBar: AppBar(title: const Text('Student profile')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Text(_error!, textAlign: TextAlign.center),
                  ),
                )
              : _student == null
                  ? const SizedBox.shrink()
                  : ListView(
                      padding: const EdgeInsets.fromLTRB(16, 14, 16, 28),
                      children: [
                        Row(
                          children: [
                            CircleAvatar(
                              radius: 28,
                              backgroundColor: AppColors.tealSoft,
                              child: Text(
                                _initials(_student!.fullName),
                                style: const TextStyle(
                                  color: AppColors.teal,
                                  fontSize: 18,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    _student!.fullName,
                                    style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w700),
                                  ),
                                  Text(
                                    '${_student!.admissionNo} · ${_student!.className} ${_student!.sectionName}',
                                    style: const TextStyle(color: AppColors.muted, fontSize: 11),
                                  ),
                                  const SizedBox(height: 4),
                                  Container(
                                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                                    decoration: BoxDecoration(
                                      color: AppColors.tealSoft,
                                      borderRadius: BorderRadius.circular(20),
                                    ),
                                    child: Text(
                                      _student!.status,
                                      style: const TextStyle(
                                        color: AppColors.teal,
                                        fontSize: 9,
                                        fontWeight: FontWeight.w700,
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 18),
                        if (_ledger != null) ...[
                          Row(
                            children: [
                              Expanded(
                                child: _MiniStat(
                                  label: 'Billed',
                                  value: money.format(_ledger!.totalBilled),
                                  color: AppColors.navy,
                                ),
                              ),
                              const SizedBox(width: 8),
                              Expanded(
                                child: _MiniStat(
                                  label: 'Balance',
                                  value: money.format(_ledger!.balance),
                                  color: _ledger!.balance > 0 ? AppColors.red : AppColors.green,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 14),
                        ],
                        Card(
                          child: Padding(
                            padding: const EdgeInsets.all(15),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                const Text(
                                  'Personal details',
                                  style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14),
                                ),
                                const SizedBox(height: 12),
                                _DetailRow(
                                  label: 'Date of birth',
                                  value: date.format(DateTime.parse(_student!.dateOfBirth)),
                                ),
                                _DetailRow(label: 'Gender', value: _student!.gender),
                                _DetailRow(label: 'Roll no.', value: _student!.rollNo ?? '—'),
                                _DetailRow(label: 'Phone', value: _student!.phone ?? '—'),
                                _DetailRow(label: 'Email', value: _student!.email ?? '—'),
                                _DetailRow(
                                  label: 'Address',
                                  value: [
                                    _student!.address,
                                    _student!.city,
                                    _student!.state,
                                  ].where((part) => part != null && part.isNotEmpty).join(', '),
                                ),
                              ],
                            ),
                          ),
                        ),
                        const SizedBox(height: 14),
                        Card(
                          child: Padding(
                            padding: const EdgeInsets.all(15),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                const Text(
                                  'Guardians',
                                  style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14),
                                ),
                                const SizedBox(height: 8),
                                if (_student!.guardians.isEmpty)
                                  const Text(
                                    'No guardians linked.',
                                    style: TextStyle(color: AppColors.muted, fontSize: 12),
                                  )
                                else
                                  ..._student!.guardians.map(
                                    (guardian) => Padding(
                                      padding: const EdgeInsets.only(top: 8),
                                      child: Row(
                                        children: [
                                          const Icon(
                                            Icons.person_outline,
                                            size: 17,
                                            color: AppColors.teal,
                                          ),
                                          const SizedBox(width: 8),
                                          Expanded(
                                            child: Text(
                                              '${guardian.fullName} (${guardian.relation})',
                                              style: const TextStyle(fontSize: 12),
                                            ),
                                          ),
                                          Text(
                                            guardian.phone,
                                            style: const TextStyle(
                                              color: AppColors.muted,
                                              fontSize: 11,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                              ],
                            ),
                          ),
                        ),
                        if (_ledger != null && _ledger!.installments.isNotEmpty) ...[
                          const SizedBox(height: 14),
                          Card(
                            child: Padding(
                              padding: const EdgeInsets.all(15),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  const Text(
                                    'Fee installments',
                                    style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14),
                                  ),
                                  const SizedBox(height: 8),
                                  ..._ledger!.installments.map(
                                    (installment) => Padding(
                                      padding: const EdgeInsets.only(top: 7),
                                      child: Row(
                                        children: [
                                          Icon(
                                            installment.isOverdue
                                                ? Icons.error_outline
                                                : Icons.schedule,
                                            size: 15,
                                            color: installment.isOverdue
                                                ? AppColors.red
                                                : AppColors.muted,
                                          ),
                                          const SizedBox(width: 7),
                                          Expanded(
                                            child: Text(
                                              installment.name,
                                              style: const TextStyle(fontSize: 12),
                                            ),
                                          ),
                                          Text(
                                            money.format(installment.balance),
                                            style: const TextStyle(
                                              fontSize: 12,
                                              fontWeight: FontWeight.w600,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ],
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

class _MiniStat extends StatelessWidget {
  const _MiniStat({required this.label, required this.value, required this.color});

  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(13),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              label.toUpperCase(),
              style: const TextStyle(
                color: AppColors.muted,
                fontSize: 9,
                letterSpacing: 0.5,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              value,
              style: TextStyle(color: color, fontSize: 17, fontWeight: FontWeight.w700),
            ),
          ],
        ),
      ),
    );
  }
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    if (value.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 7),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 105,
            child: Text(
              label,
              style: const TextStyle(color: AppColors.muted, fontSize: 11),
            ),
          ),
          Expanded(
            child: Text(value, style: const TextStyle(fontSize: 12)),
          ),
        ],
      ),
    );
  }
}
