import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/api_client.dart';
import '../core/auth_controller.dart';
import '../core/live_notifications.dart';
import '../core/models.dart';
import '../core/theme.dart';
import 'attendance_screen.dart';
import 'fees_screen.dart';
import 'my_attendance_screen.dart';
import 'notifications_screen.dart';
import 'results_screen.dart';
import 'students_screen.dart';
import 'web_portal_screen.dart';

const _genericError = 'Something went wrong. Please try again.';

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  final _scaffoldKey = GlobalKey<ScaffoldState>();
  Dashboard? _dashboard;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
    // Rings the bell for new notifications while this screen is open.
    LiveNotifications.instance.start(context.read<AuthController>());
  }

  @override
  void dispose() {
    LiveNotifications.instance.stop();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final response =
          await context.read<AuthController>().api.get('dashboard');
      if (!mounted) return;
      setState(() {
        _dashboard = Dashboard.fromJson(response as Map<String, dynamic>);
        _loading = false;
      });
    } on ApiException catch (exception) {
      if (!mounted) return;
      setState(() => _error = exception.message);
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = _genericError);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _logout() async {
    await context.read<AuthController>().signOut();
  }

  void _open(Widget screen) {
    Navigator.of(context)
        .push(MaterialPageRoute(builder: (_) => screen))
        .then((_) {
      // Returning from the inbox means the count may have changed.
      if (mounted) LiveNotifications.instance.refreshUnread();
    });
  }

  /// Web portal sections without a native screen, gated exactly as on the web.
  List<Widget> _portalTiles(AuthController auth) {
    final isSuperAdmin = auth.user?.roleCode == 'SuperAdmin';
    final items = <(IconData, String, String, bool)>[
      (
        Icons.how_to_reg_outlined,
        'Admissions',
        '/admissions',
        auth.can('admissions.manage')
      ),
      (
        Icons.assignment_ind_outlined,
        'Class & teachers',
        '/assignments',
        auth.can('settings.manage')
      ),
      (Icons.quiz_outlined, 'Examinations', '/exams', auth.can('exams.read')),
      (Icons.menu_book_outlined, 'Homework', '/homework', true),
      (
        Icons.bar_chart_outlined,
        'Reports',
        '/reports',
        auth.can('reports.read')
      ),
      (Icons.badge_outlined, 'People & HR', '/hr', auth.can('hr.read')),
      (
        Icons.directions_bus_outlined,
        'Transport',
        '/transport',
        auth.can('transport.manage')
      ),
      (
        Icons.local_library_outlined,
        'Library',
        '/library',
        auth.can('library.manage')
      ),
      (
        Icons.inventory_2_outlined,
        'Inventory',
        '/inventory',
        auth.can('inventory.manage')
      ),
      (
        Icons.class_outlined,
        'Classes & subjects',
        '/settings',
        auth.can('settings.manage')
      ),
      (
        Icons.manage_accounts_outlined,
        'Users & roles',
        '/users',
        auth.can('users.manage')
      ),
      (Icons.apartment_outlined, 'Schools', '/schools', isSuperAdmin),
    ].where((item) => item.$4).toList();

    if (items.isEmpty) return const [];
    return [
      const Padding(
        padding: EdgeInsets.fromLTRB(16, 14, 16, 4),
        child: Text('ADMINISTRATION',
            style: TextStyle(
                color: AppColors.muted,
                fontSize: 11,
                fontWeight: FontWeight.w700,
                letterSpacing: 1.2)),
      ),
      for (final item in items)
        _DrawerTile(
          icon: item.$1,
          label: item.$2,
          onTap: () => _open(WebPortalScreen(title: item.$2, route: item.$3)),
        ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthController>();
    final user = auth.user;

    return Scaffold(
      key: _scaffoldKey,
      appBar: AppBar(
        leading: Builder(
          builder: (context) => IconButton(
            icon: const Icon(Icons.menu),
            onPressed: () => Scaffold.of(context).openDrawer(),
          ),
        ),
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(user?.schoolName ?? 'School workspace',
                style: const TextStyle(fontSize: 15)),
            Text(
              user?.role ?? '',
              style: const TextStyle(
                  color: AppColors.muted,
                  fontSize: 11,
                  fontWeight: FontWeight.w400),
            ),
          ],
        ),
        actions: [
          _BellButton(
            onPressed: () => _open(const NotificationsScreen()),
          ),
          Padding(
            padding: const EdgeInsets.only(right: 14),
            child: CircleAvatar(
              radius: 17,
              backgroundColor: AppColors.tealSoft,
              child: Text(
                _initials(user?.fullName ?? ''),
                style: const TextStyle(
                  color: AppColors.teal,
                  fontSize: 12,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ),
        ],
      ),
      drawer: Drawer(
        child: SafeArea(
          child: Column(
            children: [
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(20),
                decoration: const BoxDecoration(
                  gradient: LinearGradient(
                    colors: [AppColors.navyDeep, AppColors.navy],
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                  ),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    CircleAvatar(
                      radius: 24,
                      backgroundColor: AppColors.gold,
                      child: Text(
                        _initials(user?.fullName ?? ''),
                        style: const TextStyle(
                          color: AppColors.navy,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      user?.fullName ?? '',
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    Text(
                      user?.email ?? '',
                      style: const TextStyle(
                          color: Color(0xFF9FB7CB), fontSize: 12),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      '${user?.schoolName ?? ''} · ${user?.role ?? ''}',
                      style: const TextStyle(
                          color: Color(0xFFC6D7E6), fontSize: 11),
                    ),
                  ],
                ),
              ),
              Expanded(
                child: ListView(
                  padding: EdgeInsets.zero,
                  children: [
                    _DrawerTile(
                      icon: Icons.people_outline,
                      label: 'Students',
                      enabled: auth.can('students.read'),
                      onTap: () => _open(const StudentsScreen()),
                    ),
                    _DrawerTile(
                      icon: Icons.fact_check_outlined,
                      label: 'Attendance',
                      enabled: auth.can('attendance.read'),
                      onTap: () => _open(const AttendanceScreen()),
                    ),
                    _DrawerTile(
                      icon: Icons.schedule_outlined,
                      label: 'My attendance',
                      enabled: auth.can('attendance.mark'),
                      onTap: () => _open(const MyAttendanceScreen()),
                    ),
                    _DrawerTile(
                      icon: Icons.payments_outlined,
                      label: 'Fees',
                      enabled: auth.can('fees.read'),
                      onTap: () => _open(const FeesScreen()),
                    ),
                    _DrawerTile(
                      icon: Icons.school_outlined,
                      label: 'Results',
                      enabled: auth.can('exams.read'),
                      onTap: () => _open(const ResultsScreen()),
                    ),
                    _DrawerTile(
                      icon: Icons.notifications_none,
                      label: 'Notifications',
                      onTap: () => _open(const NotificationsScreen()),
                    ),
                    ..._portalTiles(auth),
                  ],
                ),
              ),
              const Divider(height: 1),
              ListTile(
                leading: const Icon(Icons.logout, color: AppColors.red),
                title: const Text('Sign out',
                    style: TextStyle(color: AppColors.red)),
                onTap: _logout,
              ),
            ],
          ),
        ),
      ),
      body: RefreshIndicator(
        onRefresh: _load,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 28),
          children: [
            Text(
              _greeting(),
              style: const TextStyle(color: AppColors.muted, fontSize: 13),
            ),
            const SizedBox(height: 3),
            Text(
              _firstName(user?.fullName ?? 'there'),
              style: const TextStyle(
                color: AppColors.ink,
                fontSize: 25,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              _dashboard?.academicSession ?? 'Current academic session',
              style: const TextStyle(color: AppColors.muted, fontSize: 12),
            ),
            const SizedBox(height: 18),
            if (_loading)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 34),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (_error != null)
              _ErrorCard(message: _error!, onRetry: _load)
            else ...[
              GridView.builder(
                shrinkWrap: true,
                physics: const NeverScrollableScrollPhysics(),
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                  crossAxisCount: 2,
                  crossAxisSpacing: 10,
                  mainAxisSpacing: 10,
                  childAspectRatio: 1.55,
                ),
                itemCount: _dashboard?.cards.length ?? 0,
                itemBuilder: (context, index) {
                  final card = _dashboard!.cards[index];
                  return _StatCard(card: card);
                },
              ),
              const SizedBox(height: 22),
              const Text(
                'Quick actions',
                style: TextStyle(
                  color: AppColors.ink,
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  if (auth.can('attendance.read'))
                    _QuickTile(
                      icon: Icons.fact_check_outlined,
                      label: 'Attendance',
                      onTap: () => _open(const AttendanceScreen()),
                    ),
                  if (auth.can('attendance.mark'))
                    _QuickTile(
                      icon: Icons.schedule_outlined,
                      label: 'Check in',
                      onTap: () => _open(const MyAttendanceScreen()),
                    ),
                  if (auth.can('fees.read'))
                    _QuickTile(
                      icon: Icons.payments_outlined,
                      label: 'Fees',
                      onTap: () => _open(const FeesScreen()),
                    ),
                  if (auth.can('exams.read'))
                    _QuickTile(
                      icon: Icons.emoji_events_outlined,
                      label: 'Results',
                      onTap: () => _open(const ResultsScreen()),
                    ),
                  if (auth.can('students.read'))
                    _QuickTile(
                      icon: Icons.people_outline,
                      label: 'Students',
                      onTap: () => _open(const StudentsScreen()),
                    ),
                  _QuickTile(
                    icon: Icons.notifications_none,
                    label: 'Notices',
                    onTap: () => _open(const NotificationsScreen()),
                  ),
                ],
              ),
              const SizedBox(height: 22),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Text(
                    'Recent activity',
                    style: TextStyle(
                      color: AppColors.ink,
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  TextButton(
                    onPressed: () => _open(const NotificationsScreen()),
                    child: const Text('View all'),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              if ((_dashboard?.activity.isEmpty ?? true))
                const _Empty(text: 'No activity recorded yet.')
              else
                ..._dashboard!.activity.take(5).map(
                      (item) => Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Container(
                              margin: const EdgeInsets.only(top: 5),
                              width: 8,
                              height: 8,
                              decoration: BoxDecoration(
                                color: _toneColor(item.tone),
                                shape: BoxShape.circle,
                              ),
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    item.title,
                                    style: const TextStyle(
                                        fontWeight: FontWeight.w600,
                                        fontSize: 13),
                                  ),
                                  if (item.detail != null)
                                    Text(
                                      item.detail!,
                                      style: const TextStyle(
                                          color: AppColors.muted, fontSize: 11),
                                    ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
            ],
          ],
        ),
      ),
    );
  }

  String _greeting() {
    final hour = DateTime.now().hour;
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }

  String _firstName(String name) {
    final parts = name.split(' ').where((p) => p.isNotEmpty).toList();
    return parts.isEmpty ? 'there' : parts.first;
  }

  String _initials(String name) => name
      .split(' ')
      .where((part) => part.isNotEmpty)
      .take(2)
      .map((part) => part[0].toUpperCase())
      .join();

  Color _toneColor(String tone) {
    switch (tone) {
      case 'positive':
        return AppColors.green;
      case 'warning':
        return AppColors.amber;
      case 'danger':
        return AppColors.red;
      default:
        return AppColors.teal;
    }
  }
}

/// Bell icon with the unread count badge, live from the notification feed.
class _BellButton extends StatelessWidget {
  const _BellButton({required this.onPressed});

  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: LiveNotifications.instance,
      builder: (context, _) {
        final unread = LiveNotifications.instance.unread;
        return IconButton(
          onPressed: onPressed,
          icon: Badge(
            isLabelVisible: unread > 0,
            backgroundColor: AppColors.red,
            label: Text(
              unread > 99 ? '99+' : '$unread',
              style: const TextStyle(fontSize: 9, color: Colors.white),
            ),
            child: const Icon(Icons.notifications_none),
          ),
        );
      },
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({required this.card});

  final DashboardCard card;

  @override
  Widget build(BuildContext context) {
    final color = switch (card.tone) {
      'positive' => AppColors.green,
      'warning' => AppColors.amber,
      'danger' => AppColors.red,
      'info' => AppColors.teal,
      _ => AppColors.muted,
    };

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(13),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Container(
                    width: 7,
                    height: 7,
                    decoration:
                        BoxDecoration(color: color, shape: BoxShape.circle)),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    card.label.toUpperCase(),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppColors.muted,
                      fontSize: 9,
                      letterSpacing: 0.5,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
              ],
            ),
            const Spacer(),
            RichText(
              text: TextSpan(
                style: const TextStyle(
                    color: AppColors.ink,
                    fontSize: 22,
                    fontWeight: FontWeight.w700),
                children: [
                  TextSpan(text: card.value),
                  if (card.unit != null)
                    TextSpan(
                      text: ' ${card.unit}',
                      style: const TextStyle(
                          color: AppColors.muted,
                          fontSize: 11,
                          fontWeight: FontWeight.w500),
                    ),
                ],
              ),
            ),
            if (card.hint != null)
              Text(
                card.hint!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(color: AppColors.muted, fontSize: 10),
              ),
          ],
        ),
      ),
    );
  }
}

class _QuickTile extends StatelessWidget {
  const _QuickTile(
      {required this.icon, required this.label, required this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: (MediaQuery.of(context).size.width - 42) / 2,
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: AppColors.line),
        ),
        child: Row(
          children: [
            Icon(icon, color: AppColors.teal, size: 20),
            const SizedBox(width: 9),
            Text(label,
                style:
                    const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
          ],
        ),
      ),
    );
  }
}

class _DrawerTile extends StatelessWidget {
  const _DrawerTile({
    required this.icon,
    required this.label,
    required this.onTap,
    this.enabled = true,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    if (!enabled) return const SizedBox.shrink();
    return ListTile(
      leading: Icon(icon, color: AppColors.navy),
      title: Text(label, style: const TextStyle(fontSize: 14)),
      onTap: onTap,
    );
  }
}

class _ErrorCard extends StatelessWidget {
  const _ErrorCard({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          children: [
            const Icon(Icons.cloud_off, color: AppColors.red, size: 28),
            const SizedBox(height: 8),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 12),
            OutlinedButton(onPressed: onRetry, child: const Text('Try again')),
          ],
        ),
      ),
    );
  }
}

class _Empty extends StatelessWidget {
  const _Empty({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(22),
        child: Center(
          child: Text(text,
              style: const TextStyle(color: AppColors.muted, fontSize: 12)),
        ),
      ),
    );
  }
}
