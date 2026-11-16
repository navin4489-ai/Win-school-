# Local end-to-end QA sweep

`e2e.mjs` is a repeatable, self-contained HTTP sweep of the 13 QA areas
(bootstrap login, multi-tenant setup, search, tenant isolation, RBAC, parent
login, fees, receipt PDFs, notifications + SSE stream, file uploads, security
headers/401s/rate limits, auth hardening, injection-style searches).

It talks **only** to a locally running instance with an isolated in-memory
database. Never point it at a shared or production host.

## Run

```powershell
# terminal 1 — start the API with a throwaway database
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Database__Provider='InMemory'
$env:ASPNETCORE_URLS='http://127.0.0.1:5088'
$env:Bootstrap__AdminEmail='e2e-admin@local.test'
$env:Bootstrap__AdminPassword='E2ePassword12345!'
$env:Bootstrap__SchoolCode='E2E'
$env:Bootstrap__SchoolName='E2E Test School'
dotnet run --project backend/src/SchoolErp.Api --no-build --no-launch-profile
```

```powershell
# terminal 2 — from the repository root
node backend/tests/e2e-local/e2e.mjs
```

Exit code 0 = every check passed. Restart the API between runs: the in-memory
database keeps the schools/users created by a previous run and the setup step
will fail on duplicate school codes.

The login rate-limit probe runs last and intentionally sends 12 bad logins
against a non-existent email, so keep any follow-up logins for at least a minute
afterwards.

## One-time migration: sign existing /files/db/ URLs

`sign-existing-file-urls.mjs` back-fills signatures onto file URLs that were stored
before signed URLs were switched on. After `Files:SignedFromUtc` passes, an unsigned
`/files/db/{id}` request is refused, so any bare URL already persisted in the database
(student photos, school logos, homework attachments, attendance photos, study materials,
documents, …) must get a `?sig=...` appended or it stops resolving.

It is **idempotent** (skips values already containing `?sig=`), **dry-run by default**
(pass `--apply` to write), and **self-discovering**: it scans every text/varchar column of
every table via `information_schema` for the literal `/files/db/`, so it needs no hand-kept
column list. It computes the exact same HMAC as the C# `FileUrlSigner`
(`FileUrlSignerTests.Compute_MatchesKnownVector` pins the shared vector
`926dbaff777cb3f0900745274ba1e68d`).

Requires `psql` on PATH and Node (no npm packages). The DB connection comes from
`DATABASE_URL`; the secret from `FILES_SECRET`, falling back to `JWT_SIGNING_KEY` — the
same `Files:Secret -> Jwt:SigningKey` fallback the server uses.

> ⚠️ **Never run this against production.** Take a backup and test on a copy first.

```powershell
$env:DATABASE_URL = 'postgres://user:pass@host:5432/dbname'   # a throwaway copy, not prod
$env:FILES_SECRET = '<same value as Files__Secret; omit to use JWT_SIGNING_KEY>'

# 1) dry run — prints every row it would change, writes nothing
node backend/tests/e2e-local/sign-existing-file-urls.mjs

# 2) apply — only after reviewing the dry-run output
node backend/tests/e2e-local/sign-existing-file-urls.mjs --apply
```

The script refuses to start unless both `DATABASE_URL` and a secret are present.
