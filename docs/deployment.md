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

## Health and monitoring

| Signal | Alert |
| --- | --- |
| `/health` unhealthy | Page on-call immediately |
| HTTP 5xx > 1% (5 min) | Sev 2 |
| p95 latency > 2 s (10 min) | Sev 3 |
| Failed logins > 50 / 5 min | Security review |
| Notification outbox backlog > 100 | Check provider |
| SQL DTU/CPU > 80% (15 min) | Scale or optimise |
| Backup failure | Sev 1 |

## Go-live checklist

- [ ] DNS + TLS certificates; HSTS on.
- [ ] CORS origins limited to portal domains.
- [ ] Production JWT key, seed disabled, demo accounts removed.
- [ ] Backups configured and a restore has been rehearsed.
- [ ] Smoke test: login, admission, fee payment, attendance, report export.
- [ ] UAT sign-off recorded (`docs/uat-checklist.md`).
- [ ] Support rota and rollback owner named.
