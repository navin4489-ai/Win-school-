# Backs up the production PostgreSQL database to a local file.
#
# Usage (PowerShell):
#   ./scripts/backup-db.ps1 -DatabaseUrl "postgres://user:pass@host:5432/db" [-OutDir C:\Backups] [-PgBin C:\pgsql\bin]
#
# The External Database URL is on the Render dashboard: school-erp-db → Connect.
# Restore into a fresh database with:
#   pg_restore --no-owner --no-privileges --dbname "<target url>" <file>.dump
param(
    [Parameter(Mandatory = $true)][string]$DatabaseUrl,
    [string]$OutDir = "$HOME\Documents\WintechSchool-backups",
    [string]$PgBin = ""
)

$ErrorActionPreference = "Stop"

$pgDump = if ($PgBin) { Join-Path $PgBin "pg_dump.exe" } else { (Get-Command pg_dump -ErrorAction SilentlyContinue).Source }
if (-not $pgDump -or -not (Test-Path $pgDump)) {
    throw "pg_dump (PostgreSQL 18 or newer) was not found. Install the PostgreSQL client tools or pass -PgBin."
}

if ($DatabaseUrl -notmatch "sslmode=") {
    $DatabaseUrl += $(if ($DatabaseUrl.Contains("?")) { "&" } else { "?" }) + "sslmode=require"
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$file = Join-Path $OutDir ("schoolerp-{0}.dump" -f (Get-Date -Format "yyyyMMdd-HHmm"))

& $pgDump --format=custom --no-owner --no-privileges --file=$file $DatabaseUrl
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE" }

$sizeKb = [math]::Round((Get-Item $file).Length / 1KB)
Write-Host "Backup written to $file ($sizeKb KB)"

# Keep the 30 most recent backups.
Get-ChildItem $OutDir -Filter "schoolerp-*.dump" | Sort-Object LastWriteTime -Descending |
    Select-Object -Skip 30 | Remove-Item -Force
