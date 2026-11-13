# Deployment runbook

## Environments

| Environment | Purpose | Data |
| --- | --- | --- |
| Local | Development | Seeded demo school |
| Staging | UAT + DAST | Anonymised copy |
| Production | Live schools | Encrypted, backed up |

## Azure resources

```
rg-schoolerp-prod
├── plan-schoolerp-prod          App Service (Linux, P1v3, autoscale 2–10)
├── app-schoolerp-api-prod       API, staging slot
├── app-schoolerp-web-prod       Angular static/SSR
├── sql-schoolerp-prod           Azure SQL, zone redundant
├── stschoolerpdocuments         Blob, private container
├── redis-schoolerp-prod         Azure Cache for Redis
├── kv-schoolerp-prod            Key Vault (RBAC)
├── appi-schoolerp-prod          Application Insights
└── comm-schoolerp-prod          Communication Services (email/SMS)
```

Provision with Bicep/Terraform (Phase 10) or the portal for the pilot. Required app settings:

```
ConnectionStrings__Default=@Microsoft.KeyVault(SecretUri=https://kv-schoolerp-prod.vault.azure.net/secrets/sql/)
Jwt__Key=@Microsoft.KeyVault(...)
Jwt__Issuer=school-erp
Jwt__Audience=school-erp-clients
Storage__Provider=azure
Storage__ConnectionString=@Microsoft.KeyVault(...)
Seed__Enabled=false
ASPNETCORE_ENVIRONMENT=Production
```

## Database deployment

1. Generate an idempotent script from migrations:
   `dotnet ef migrations script --idempotent -o database/migrations.sql`
2. Review in staging; take a restore point.
3. Apply through the pipeline using a managed identity, never `sa`.
4. Run `database/seed.sql` only for the first school and reference data.

Rollback: restore point + previous app slot swap. Destructive migrations require an explicit
expand/contract two-release pattern.

## CI/CD

- `.github/workflows/ci.yml` builds and tests API, web and mobile on every PR.
- `infra/azure-pipelines.yml` builds artifacts, runs `dotnet test`, publishes, deploys to staging,
  waits for approval, swaps production.
- Container option: `infra/Dockerfile.api`, `infra/Dockerfile.web`, `infra/docker-compose.yml`.

## Background jobs

Two hosted services run inside the API process and must be considered before scaling out.

| Job | What it does | Multi-node behaviour |
| --- | --- | --- |
| `NotificationDispatcher` | Drains pending notifications to the SMS/email/WhatsApp providers | Safe on several nodes: the rows are claimed and marked sent, but two nodes can pick up the same row within one 15 second window. Move the claim into a `UPDATE … OUTPUT` or a queue before running more than one instance. |
| `FeePenaltyService` | Accrues the daily late fee on unpaid installments | Safe on several nodes: the unique index on `(FeeInstallmentId, PenaltyDate)` makes a second node's insert fail and the day is charged exactly once. |
| `NotificationHub` | Fans notifications out to the connected browsers over server-sent events | **Process local.** With more than one instance a notification created on node A is not pushed to a browser connected to node B. Replace the hub with Redis pub/sub or Azure SignalR Service before scaling out, or pin the portal to a single instance. |

The long-lived notification stream is excluded from the global rate limiter
(`[DisableRateLimiting]`), otherwise every open browser tab would hold a request permit for
the lifetime of the connection.

## Health and monitoring

| Signal | Alert |
| --- | --- |
| `/health` unhealthy | Page on-call immediately |
| HTTP 5xx > 1% (5 min) | Sev 2 |
| p95 latency > 2 s (10 min) | Sev 3 |
| Failed logins > 50 / 5 min | Security review |
| Notification outbox backlog > 100 | Check provider |
| Late-fee rows per day far above the usual | Check the penalty policy and the `Fees:Penalty` settings |
| SQL DTU/CPU > 80% (15 min) | Scale or optimise |
| Backup failure | Sev 1 |

## Go-live checklist

- [ ] DNS + TLS certificates; HSTS on.
- [ ] CORS origins limited to portal domains.
- [ ] Production JWT key, seed disabled, demo accounts removed.
- [ ] `Fees:Penalty` policy reviewed with the school and set to the agreed daily amount and cap.
- [ ] A real payment provider configured, or the dummy gateway explicitly accepted for the launch.
- [ ] SMS / email / WhatsApp providers configured (`LogNotificationSender` only writes to the log).
- [ ] `NotificationHub` replaced with a shared broker if more than one API instance is planned.
- [ ] Mobile camera and location permissions declared in the Android manifest and the iOS plist.
- [ ] Backups configured and a restore has been rehearsed.
- [ ] Smoke test: login, admission, fee payment, attendance, report export.
- [ ] UAT sign-off recorded (`docs/uat-checklist.md`).
- [ ] Support rota and rollback owner named.
