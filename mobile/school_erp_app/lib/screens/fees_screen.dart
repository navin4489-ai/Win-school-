import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';

class FeesScreen extends StatefulWidget {
  const FeesScreen({super.key});

  @override
  State<FeesScreen> createState() => _FeesScreenState();
}

class _FeesScreenState extends State<FeesScreen> {
  final _studentId = TextEditingController();
  Ledger? _ledger;
  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _studentId.dispose();
    super.dispose();
  }

  Future<void> _loadLedger() async {
    final id = int.tryParse(_studentId.text.trim());
    if (id == null || id <= 0) {
      setState(() => _error = 'Enter a valid student ID.');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final response = await context.read<AuthController>().api.get('fees/students/$id/ledger');
      if (!mounted) return;
      setState(() {
        _ledger = Ledger.fromJson(response as Map<String, dynamic>);
        _loading = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _ledger = null;
        _error = exception.message;
        _loading = false;
      });
    }
  }

  Future<void> _recordPayment(Installment? installment) async {
    final ledger = _ledger;
    if (ledger == null) return;

    final result = await showModalBottomSheet<Map<String, dynamic>>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _PaymentSheet(
        suggestedAmount: installment?.balance ?? ledger.balance,
      ),
    );
    if (result == null || !mounted) return;

    setState(() => _loading = true);
    try {
      final payment = await context.read<AuthController>().api.post('fees/payments', body: {
        'studentId': ledger.studentId,
        'feeInstallmentId': installment?.id,
        'amount': result['amount'],
        'mode': result['mode'],
        'idempotencyKey': DateTime.now().microsecondsSinceEpoch.toString(),
      }) as Map<String, dynamic>;

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Receipt ${payment['receiptNo']} recorded for ₹${payment['amount']}.',
          ),
        ),
      );
      await _loadLedger();
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = exception.message;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final canCollect = context.read<AuthController>().can('fees.collect');
    final money = NumberFormat.currency(locale: 'en_IN', symbol: '₹', decimalDigits: 0);

    return Scaffold(
      appBar: AppBar(title: const Text('Fees & payments')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 28),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _studentId,
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(labelText: 'Student ID'),
                      onSubmitted: (_) => _loadLedger(),
                    ),
                  ),
                  const SizedBox(width: 10),
                  FilledButton(
                    onPressed: _loading ? null : _loadLedger,
                    child: const Text('Load'),
                  ),
                ],
              ),
            ),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 10),
              child: Card(
                color: const Color(0xFFFDE8E6),
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Text(_error!, style: const TextStyle(color: AppColors.red)),
                ),
              ),
            ),
          if (_loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 40),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (_ledger == null)
            const Card(
              child: Padding(
                padding: EdgeInsets.all(26),
                child: Center(
                  child: Text(
                    'Enter a student ID to view the fee ledger.',
                    style: TextStyle(color: AppColors.muted),
                  ),
                ),
              ),
            )
          else ...[
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _MoneyCard(
                    label: 'Balance',
                    value: money.format(_ledger!.balance),
                    color: _ledger!.balance > 0 ? AppColors.red : AppColors.green,
                  ),
                ),
                const SizedBox(width: 9),
                Expanded(
                  child: _MoneyCard(
                    label: 'Overdue',
                    value: money.format(_ledger!.overdue),
                    color: AppColors.amber,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(15),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      _ledger!.studentName,
                      style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
                    ),
                    Text(
                      '${_ledger!.admissionNo} · ${_ledger!.className}',
                      style: const TextStyle(color: AppColors.muted, fontSize: 11),
                    ),
                    const SizedBox(height: 10),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text('Billed ${money.format(_ledger!.totalBilled)}',
                            style: const TextStyle(fontSize: 11, color: AppColors.muted)),
                        Text('Paid ${money.format(_ledger!.totalPaid)}',
                            style: const TextStyle(fontSize: 11, color: AppColors.muted)),
                      ],
                    ),
                    if (canCollect) ...[
                      const SizedBox(height: 12),
                      SizedBox(
                        width: double.infinity,
                        child: FilledButton(
                          onPressed: _ledger!.balance > 0 ? () => _recordPayment(null) : null,
                          child: const Text('Record payment'),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
            const SizedBox(height: 16),
            const Text(
              'Installments',
              style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            if (_ledger!.installments.isEmpty)
              const Card(
                child: Padding(
                  padding: EdgeInsets.all(20),
                  child: Center(
                    child: Text('No installments assigned.', style: TextStyle(color: AppColors.muted)),
                  ),
                ),
              )
            else
              ..._ledger!.installments.map(
                (installment) => Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: ListTile(
                    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
                    leading: Icon(
                      installment.balance <= 0
                          ? Icons.check_circle_outline
                          : installment.isOverdue
                              ? Icons.error_outline
                              : Icons.schedule,
                      color: installment.balance <= 0
                          ? AppColors.green
                          : installment.isOverdue
                              ? AppColors.red
                              : AppColors.amber,
                    ),
                    title: Text(installment.name, style: const TextStyle(fontSize: 13)),
                    subtitle: Text(
                      'Due ${DateFormat('dd MMM yyyy').format(DateTime.parse(installment.dueDate))} · ${installment.status}',
                      style: const TextStyle(fontSize: 10),
                    ),
                    trailing: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        Text(
                          money.format(installment.balance),
                          style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13),
                        ),
                        if (canCollect && installment.balance > 0)
                          TextButton(
                            onPressed: () => _recordPayment(installment),
                            style: TextButton.styleFrom(
                              padding: EdgeInsets.zero,
                              minimumSize: const Size(40, 24),
                            ),
                            child: const Text('Pay', style: TextStyle(fontSize: 11)),
                          ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ],
      ),
    );
  }
}

class _MoneyCard extends StatelessWidget {
  const _MoneyCard({required this.label, required this.value, required this.color});

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
                letterSpacing: 0.6,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 5),
            Text(
              value,
              style: TextStyle(color: color, fontSize: 19, fontWeight: FontWeight.w700),
            ),
          ],
        ),
      ),
    );
  }
}

class _PaymentSheet extends StatefulWidget {
  const _PaymentSheet({required this.suggestedAmount});

  final double suggestedAmount;

  @override
  State<_PaymentSheet> createState() => _PaymentSheetState();
}

class _PaymentSheetState extends State<_PaymentSheet> {
  late final TextEditingController _amount;
  String _mode = 'Cash';
  String? _error;

  @override
  void initState() {
    super.initState();
    _amount = TextEditingController(
      text: widget.suggestedAmount > 0 ? widget.suggestedAmount.toStringAsFixed(2) : '',
    );
  }

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  void _submit() {
    final amount = double.tryParse(_amount.text);
    if (amount == null || amount <= 0) {
      setState(() => _error = 'Enter a valid amount.');
      return;
    }
    Navigator.of(context).pop({'amount': amount, 'mode': _mode});
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(
        18,
        18,
        18,
        MediaQuery.of(context).viewInsets.bottom + 22,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Record payment',
            style: TextStyle(fontSize: 19, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _amount,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Amount (₹)'),
          ),
          const SizedBox(height: 10),
          DropdownButtonFormField<String>(
            value: _mode,
            decoration: const InputDecoration(labelText: 'Mode'),
            items: const ['Cash', 'Upi', 'Card', 'NetBanking', 'Cheque', 'BankTransfer', 'Online']
                .map((mode) => DropdownMenuItem(value: mode, child: Text(mode)))
                .toList(),
            onChanged: (value) => setState(() => _mode = value ?? 'Cash'),
          ),
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(_error!, style: const TextStyle(color: AppColors.red, fontSize: 12)),
          ],
          const SizedBox(height: 16),
          SizedBox(
            width: double.infinity,
            child: FilledButton(onPressed: _submit, child: const Text('Confirm payment')),
          ),
        ],
      ),
    );
  }
}
