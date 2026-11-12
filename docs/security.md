# Security controls

Mapped to BRD §10 and OWASP ASVS. Each control names where it lives so reviewers can verify it.

## Authentication

| Control | Implementation |
| --- | --- |
| Password hashing | `PasswordHasher<UserAccount>` (PBKDF2-SHA256, 100k iterations) in `PasswordHasherService` |
| Access tokens | JWT HS256, 30-minute lifetime, signed with `Jwt:Key` from Key Vault |
| Refresh tokens | 256-bit random, SHA-256 hash stored, 7-day lifetime, rotated per use |
| Refresh reuse detection | Reusing a revoked token revokes every active token for that user |
| Lockout | 5 failed attempts → 15-minute lockout (`AuthService`) |
| Session timeout | Short access token + refresh rotation; logout revokes refresh token |
| Password reset | Single-use hashed token endpoint (hook for email provider) |
| MFA | `TwoFactorEnabled` + TOTP secret fields reserved; enforced for Super Admin at Phase 10 |
| Secrets | Never in source; user-secrets locally, Key Vault in Azure |

## Authorization

- `PermissionPolicyProvider` + `PermissionHandler`: every controller action has an explicit permission
  policy or `[Authorize]` plus a service-level scope check.
- School isolation: EF global filter (read) and `SaveChanges` guard (write).
- Parent scope: `ParentService` resolves `StudentParents` for the authenticated guardian only.
- Teacher scope: `TeacherAssignment` checked before attendance, marks and homework mutations.
- Students: read-only endpoints; no write policy is granted to the `Student` role.
- Cross-tenant object access returns **404**, not 403, to avoid resource enumeration.

## Application security

| Threat | Control |
| --- | --- |
| SQL injection | EF Core parameterisation; no string-concatenated SQL |
| XSS | JSON API + Angular escaping; CSP header at the edge; no `innerHTML` |
| CSRF | Bearer tokens in headers, no cookie auth for APIs |
| Mass assignment | Explicit request DTOs; entities never bound from the wire |
| IDOR | Scope checks and tenant filters; opaque sequence numbers not trusted |
| Rate limiting | Global 300 req/min per IP; auth endpoints 10 req/min |
| Brute force | Lockout + generic "invalid credentials" response |
| File upload | Extension/MIME allow-list, 10 MB cap, random blob names, malware scan abstraction, download through authorised endpoint |
| SSRF | No user-supplied URLs fetched server-side |
| Deserialisation | `System.Text.Json`, no polymorphic type handling |
| Error leakage | `ProblemDetails` only; stack traces development-only |

## Data protection

- TLS 1.2+ enforced at the edge and `UseHttpsRedirection` in non-development.
- TDE/Always Encrypted for Azure SQL; backups encrypted by the platform.
- Blob containers private; access only through short-lived SAS generated after authorization.
- Sensitive columns (`PasswordHash`, gateway secrets, medical notes) excluded from audit snapshots and
  masked in logs.
- Data retention: audit logs 7 years, documents per school policy, deletion via soft delete then a
  scheduled purge job with approval.
- Exports are permission-gated (`reports.read`) and logged with row counts.

## Audit trail

`AuditLogs` captures: actor id/email, school, action, entity name/id, before/after JSON (scrubbed),
IP address, user agent, correlation id and UTC timestamp. `AppDbContext.SaveChanges` writes entries
for all `IAuditable` mutations except `AuditLog` itself. Login success/failure, token refresh, payment,
refund, marks approval, attendance correction and permission changes are explicitly audited.

## Verification activities

- SAST: `dotnet format --verify-no-changes`, Roslyn analyzers, `npm audit`, `flutter analyze`.
- Dependency scanning: Dependabot + `dotnet list package --vulnerable`.
- DAST: OWASP ZAP baseline against staging before each release.
- Penetration test before Phase 10 sign-off, focused on IDOR, fee manipulation and file upload.
- Log review runbook: failed-login spikes, 5xx rate, gateway mismatch, audit gaps.

## Production checklist

- [ ] Replace demo JWT key and disable seed data.
- [ ] Key Vault references for SQL, JWT, storage, SMS, WhatsApp, payment gateway.
- [ ] Managed identities for SQL and Blob; no connection-string credentials where avoidable.
- [ ] Azure Front Door WAF, TLS-only, HSTS, CSP, rate limits.
- [ ] App Insights alerts: 5xx > 1%, p95 > 2 s, failed logins > 50/5 min, outbox backlog > 100.
- [ ] Backups: PITR 35 days, geo-redundant, quarterly restore drill.
- [ ] Secrets rotation schedule and break-glass Super Admin procedure documented.
