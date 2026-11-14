import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../core/auth_controller.dart';
import '../core/theme.dart';

/// Opens a section of the web portal inside the app, already signed in with the
/// app's session. Used for admin features that have no native screen.
class WebPortalScreen extends StatefulWidget {
  const WebPortalScreen({super.key, required this.title, required this.route});

  final String title;
  final String route;

  @override
  State<WebPortalScreen> createState() => _WebPortalScreenState();
}

class _WebPortalScreenState extends State<WebPortalScreen> {
  late final WebViewController _controller;
  bool _loading = true;
  bool _sessionInjected = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(const Color(0xFFF4F7FB))
      ..setNavigationDelegate(NavigationDelegate(
        onPageFinished: _onPageFinished,
        // Payment pages may try app links (upi://, intent://) the web view cannot open;
        // block them so the checkout stays usable with its other payment methods.
        onNavigationRequest: (request) {
          final scheme = Uri.tryParse(request.url)?.scheme.toLowerCase() ?? '';
          const allowed = {'http', 'https', 'about', 'data', 'blob', 'javascript'};
          return allowed.contains(scheme) ? NavigationDecision.navigate : NavigationDecision.prevent;
        },
        onWebResourceError: (error) {
          if ((error.isForMainFrame ?? true) && mounted) {
            setState(() {
              _loading = false;
              _error = 'Could not open this page. Check your internet connection.';
            });
          }
        },
      ));
    _start();
  }

  Future<void> _start() async {
    final auth = context.read<AuthController>();
    try {
      final token = await auth.freshAccessToken();
      if (!mounted) return;
      if (token == null || auth.user == null) {
        setState(() {
          _loading = false;
          _error = 'Your session has expired. Please sign in again.';
        });
        return;
      }
      // Load a static file on the portal's origin first so its storage can be primed.
      await _controller.loadRequest(Uri.parse('${auth.webUrl}/logo.svg'));
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Something went wrong. Please try again.';
      });
    }
  }

  Future<void> _onPageFinished(String url) async {
    if (!mounted) return;
    final auth = context.read<AuthController>();

    if (!_sessionInjected) {
      _sessionInjected = true;
      final token = auth.api.accessToken ?? '';
      final user = auth.rawUserJson ?? jsonEncode(auth.user!.toJson());
      // The refresh token is deliberately not shared: rotating it in the web view
      // would sign the app out. The page reloads with a fresh token on each visit.
      await _controller.runJavaScript('''
        localStorage.setItem('school_erp_access', ${jsonEncode(token)});
        localStorage.setItem('school_erp_user', ${jsonEncode(user)});
        localStorage.removeItem('school_erp_refresh');
      ''');
      await _controller.loadRequest(Uri.parse('${auth.webUrl}${widget.route}'));
      return;
    }

    setState(() => _loading = false);
  }

  Future<bool> _handleBack() async {
    if (await _controller.canGoBack()) {
      await _controller.goBack();
      return false;
    }
    return true;
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) async {
        if (didPop) return;
        if (await _handleBack() && context.mounted) Navigator.of(context).pop();
      },
      child: Scaffold(
        appBar: AppBar(
          title: Text(widget.title),
          actions: [
            IconButton(
              icon: const Icon(Icons.refresh),
              tooltip: 'Reload',
              onPressed: () {
                setState(() {
                  _loading = true;
                  _error = null;
                  _sessionInjected = false;
                });
                _start();
              },
            ),
          ],
        ),
        body: Stack(
          children: [
            if (_error == null) WebViewWidget(controller: _controller),
            if (_error != null)
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(_error!,
                      textAlign: TextAlign.center,
                      style: const TextStyle(color: AppColors.muted)),
                ),
              ),
            if (_loading && _error == null)
              const ColoredBox(
                color: Color(0xFFF4F7FB),
                child: Center(child: CircularProgressIndicator()),
              ),
          ],
        ),
      ),
    );
  }
}
