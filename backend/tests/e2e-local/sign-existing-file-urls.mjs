#!/usr/bin/env node
// One-time data migration: sign existing /files/db/ URLs already stored in the database.
//
// Why: /files/db/{id} used to be served anonymously. The app now signs those URLs
// (HMAC-SHA256, 32 hex chars) and, after the Files:SignedFromUtc grace window, refuses
// unsigned requests. Rows written before signing was switched on still hold bare URLs;
// this tool appends ?sig=... to them so they keep resolving once the grace window ends.
//
// It is:
//   * idempotent  — values that already contain "?sig=" are skipped;
//   * dry-run by default — pass --apply to actually UPDATE;
//   * self-discovering — it scans every text/varchar column of every table for the
//     literal "/files/db/" via information_schema, so new columns are covered automatically.
//
// It computes the SAME HMAC as the C# FileUrlSigner (verified by a unit test against a
// known vector), using the secret from FILES_SECRET or, if unset, JWT_SIGNING_KEY — the
// same fallback the server uses (Files:Secret -> Jwt:SigningKey).
//
// Connection string comes from DATABASE_URL (never hardcoded).
//
// !!! DO NOT RUN AGAINST PRODUCTION. Take a backup and test on a copy first. !!!
//
// Usage (PowerShell):
//   $env:DATABASE_URL='postgres://user:pass@host:5432/dbname'
//   $env:FILES_SECRET='<same as Files__Secret, or omit to use JWT_SIGNING_KEY>'
//   node backend/tests/e2e-local/sign-existing-file-urls.mjs            # dry-run
//   node backend/tests/e2e-local/sign-existing-file-urls.mjs --apply    # execute
//
// Requires psql on PATH. No npm dependencies.

import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';

const ROUTE_PREFIX = '/files/db/';
const APPLY = process.argv.includes('--apply');

const dbUrl = process.env.DATABASE_URL;
if (!dbUrl) {
  console.error('DATABASE_URL is not set. Refusing to run.');
  process.exit(2);
}
const secret = process.env.FILES_SECRET || process.env.JWT_SIGNING_KEY;
if (!secret) {
  console.error('Neither FILES_SECRET nor JWT_SIGNING_KEY is set. Refusing to run.');
  process.exit(2);
}

// Mirror of FileUrlSigner.ToCanonicalPath + Compute in C#.
function canonicalPath(value) {
  if (!value) return null;
  let v = String(value).trim();
  const cut = v.search(/[?#]/);
  if (cut >= 0) v = v.slice(0, cut);
  const scheme = v.indexOf('://');
  if (scheme >= 0) {
    const after = v.slice(scheme + 3);
    const slash = after.indexOf('/');
    v = slash < 0 ? '/' : after.slice(slash);
  }
  if (v.toLowerCase().startsWith('db/')) v = '/files/' + v;
  if (!v.startsWith('/')) v = '/' + v;
  if (!v.toLowerCase().startsWith(ROUTE_PREFIX)) return null;
  const file = v.slice(ROUTE_PREFIX.length);
  if (file.length === 0 || file.includes('/')) return null;
  return ROUTE_PREFIX + file;
}

function sign(value) {
  const canonical = canonicalPath(value);
  if (canonical === null) return null;
  const hex = crypto.createHmac('sha256', secret).update(canonical, 'utf8').digest('hex');
  return hex.slice(0, 32).toLowerCase();
}

function psql(sql) {
  const res = spawnSync('psql', [dbUrl, '-X', '-A', '-t', '-F', '\x1f', '-v', 'ON_ERROR_STOP=1', '-c', sql],
    { encoding: 'utf8' });
  if (res.status !== 0) {
    console.error('psql failed:', res.stderr || res.stdout);
    process.exit(1);
  }
  return res.stdout;
}

function ident(name) {
  return '"' + String(name).replace(/"/g, '""') + '"';
}

const columnsSql = `
  SELECT table_schema, table_name, column_name
  FROM information_schema.columns
  WHERE table_schema NOT IN ('pg_catalog','information_schema')
    AND data_type IN ('character varying','varchar','text','character','char')
  ORDER BY table_schema, table_name, column_name;`;

console.log(`Mode: ${APPLY ? 'APPLY (writing changes)' : 'DRY RUN (no changes)'}`);

const rows = psql(columnsSql).split('\n').map(l => l.trim()).filter(Boolean);
let totalCandidates = 0;
let totalUpdated = 0;
let totalSkipped = 0;

for (const row of rows) {
  const [schema, table, column] = row.split('\x1f');
  const fq = `${ident(schema)}.${ident(table)}`;
  const col = ident(column);

  // Primary key is not assumed; address rows by the column value via ctid for safety.
  const selectSql = `SELECT ctid::text, ${col} FROM ${fq} ` +
    `WHERE ${col} LIKE '%${ROUTE_PREFIX}%' AND ${col} NOT LIKE '%?sig=%';`;
  const found = psql(selectSql).split('\n').map(l => l.trim()).filter(Boolean);
  if (found.length === 0) continue;

  for (const f of found) {
    const idx = f.indexOf('\x1f');
    const ctid = f.slice(0, idx);
    const value = f.slice(idx + 1);
    const sig = sign(value);
    if (sig === null) { totalSkipped++; continue; }
    totalCandidates++;
    const newValue = value + '?sig=' + sig;
    console.log(`${schema}.${table}.${column} @${ctid}: ${value} -> ...?sig=${sig}`);
    if (APPLY) {
      const esc = newValue.replace(/'/g, "''");
      psql(`UPDATE ${fq} SET ${col} = '${esc}' WHERE ctid = '${ctid}' AND ${col} NOT LIKE '%?sig=%';`);
      totalUpdated++;
    }
  }
}

console.log(`\nCandidates: ${totalCandidates}, Updated: ${totalUpdated}, Non-db-skipped: ${totalSkipped}`);
if (!APPLY) console.log('Dry run complete. Re-run with --apply to write changes.');
