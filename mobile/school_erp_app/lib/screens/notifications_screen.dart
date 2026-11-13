import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/models.dart';
import '../core/theme.dart';

class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  List<NotificationItem> _items = [];
  bool _loading = true;
  bool _unreadOnly = false;
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
      final response = await context.read<AuthController>().api.get(
        'notifications',
        query: {'unreadOnly': _unreadOnly.toString()},
      ) as List<dynamic>;

      if (!mounted) return;
      setState(() {
        _items = response
            .whereType<Map<String, dynamic>>()
            .map(NotificationItem.fromJson)
            .toList();
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

  Future<void> _markRead(NotificationItem item) async {
    try {
      await context.read<AuthController>().api.post('notifications/${item.id}/read');
      if (!mounted) return;
      setState(() {
        _items = _items
            .map(
              (row) => row.id == item.id
                  ? NotificationItem(
                      id: row.id,
                      title: row.title,
                      message: row.message,
                      category: row.category,
                      channel: row.channel,
                      status: row.status,
                      createdAtUtc: row.createdAtUtc,
                      readAtUtc: DateTime.now().toIso8601String(),
                    )
                  : row,
            )
            .toList();
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(exception.message), backgroundColor: AppColors.red),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final date = DateFormat('dd MMM, HH:mm');

    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        actions: [
          Row(
            children: [
              const Text('Unread', style: TextStyle(fontSize: 11, color: AppColors.muted)),
              Switch(
                value: _unreadOnly,
                activeThumbColor: AppColors.teal,
                onChanged: (value) {
                  setState(() => _unreadOnly = value);
                  _load();
                },
              ),
            ],
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Text(_error!, textAlign: TextAlign.center),
                  ),
                )
              : _items.isEmpty
                  ? const Center(
                      child: Text(
                        'No notifications yet.',
                        style: TextStyle(color: AppColors.muted),
                      ),
                    )
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.builder(
                        padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
                        itemCount: _items.length,
                        itemBuilder: (context, index) {
                          final item = _items[index];
                          return Card(
                            margin: const EdgeInsets.only(bottom: 9),
                            child: Padding(
                              padding: const EdgeInsets.all(14),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Row(
                                    children: [
                                      Container(
                                        width: 7,
                                        height: 7,
                                        decoration: BoxDecoration(
                                          color: item.isUnread ? AppColors.teal : AppColors.line,
                                          shape: BoxShape.circle,
                                        ),
                                      ),
                                      const SizedBox(width: 7),
                                      Expanded(
                                        child: Text(
                                          item.title,
                                          style: const TextStyle(
                                            fontSize: 13,
                                            fontWeight: FontWeight.w700,
                                          ),
                                        ),
                                      ),
                                      Text(
                                        date.format(DateTime.parse(item.createdAtUtc).toLocal()),
                                        style: const TextStyle(
                                          color: AppColors.muted,
                                          fontSize: 9,
                                        ),
                                      ),
                                    ],
                                  ),
                                  const SizedBox(height: 5),
                                  Text(
                                    item.message,
                                    style: const TextStyle(fontSize: 12, height: 1.4),
                                  ),
                                  const SizedBox(height: 8),
                                  Row(
                                    children: [
                                      _Tag(text: item.category),
                                      const SizedBox(width: 6),
                                      _Tag(text: item.channel),
                                      const Spacer(),
                                      if (item.isUnread)
                                        TextButton(
                                          onPressed: () => _markRead(item),
                                          style: TextButton.styleFrom(
                                            minimumSize: const Size(40, 28),
                                            padding: EdgeInsets.zero,
                                          ),
                                          child: const Text(
                                            'Mark read',
                                            style: TextStyle(fontSize: 11),
                                          ),
                                        ),
                                    ],
                                  ),
                                ],
                              ),
                            ),
                          );
                        },
                      ),
                    ),
    );
  }
}

class _Tag extends StatelessWidget {
  const _Tag({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
      decoration: BoxDecoration(
        color: AppColors.canvas,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.line),
      ),
      child: Text(
        text,
        style: const TextStyle(color: AppColors.muted, fontSize: 9, fontWeight: FontWeight.w600),
      ),
    );
  }
}
