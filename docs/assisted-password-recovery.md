# BE-FE-530 - HF One assisted access recovery

R3. Issue #132. Base: `60a9fd7da7f88aa2c7be659f716b3f215595a841`.
Approved internal-pilot recovery, visible HF One branding and bounded access UX.
NO MERGE: Fernando performs manual merge after independent review, exact-HEAD CI
and short pre-merge acceptance. No BE-FE-531 work.

## Security contract

- Tenant operators need `USERS_RECOVERY_MANAGE`, issued by default only to ADMIN.
  Existing ADMIN assignments are granted by the migration; only roles actually
  receiving a new assignment increment AuthorizationVersion. No new default
  SUPERVISOR/CASHIER privileges. Current authority is rechecked after the lock.
- Operators attest identity confirmation, Reason and DeliveryReference from a
  previously accredited external procedure/channel. Stored User.Email is NOT
  identity proof. Never enter secrets into these references.
- Operator receives a cryptographically random 256-bit secret only in the initial
  authorized response. No secret retrieval endpoint. Owner chooses the password.
  New links retire prior links; status/revoke returns no secret.
- Fixed 15-minute lifetime. Database stores a SHA-256 digest bound to immutable
  namespace, challenge ID, company/user, password fingerprint, SessionVersion,
  role ID/AuthorizationVersion and operational establishment/emission point.
  Digest comparison uses CryptographicOperations.FixedTimeEquals.
- Consumption is public POST and does not use a session or client-supplied tenant.
  Wrong type, malformed, unknown, expired, revoked, used, inactive or stale links
  return `RECOVERY_INVALID`; no public user lookup or validation GET exists.
- Tenant company lock follows existing administrative lifecycle ordering. Role,
  establishment and emission-point share locks protect the state snapshot.
  Separate platform table serialization avoids actor/target lock inversion.
  PasswordHash, SessionVersion increment, consumed timestamp and audit commit in
  one PostgreSQL transaction. A second consumer/other older challenge fails.
- Revalidate active Company/User/Role/Establishment/EmissionPoint and ownership
  both when issuing and consuming. Recovery never activates or grants privileges.
- Platform recovery is a separate namespace/context: another active authenticated
  PLATFORM_ADMIN must reauthenticate using their current password for issue/revoke.
  No platform deactivation/role mutation was introduced; last admin is preserved.
- Authenticated self-change requires current password, 12..256 new characters
  without forced composition, atomic SessionVersion increment and audit. Existing
  legacy passwords can still authenticate and serve as the current password.
- Legacy `PUT /api/Users/{id}/password` now returns 409
  `ASSISTED_RECOVERY_REQUIRED`, without choosing/changing the owner's password.
- Audit: Issued, Consumed, Rejected, Revoked, PasswordChanged, actor/target/scope,
  timestamp and accredited references. No token, token digest, password or JWT.
  No-store/no-referrer sensitive responses and existing per-IP/plane rate limiter
  cover recovery and reauthentication endpoints.

## Routes and API

Tenant: `/login`, `/recover-access`, `/recover-access/complete#token=...`,
`/account/password`, Administration -> Users -> Iniciar recuperacion.
Platform: `/platform/login`, `/platform/accounts`,
`/platform/recover-access/complete#token=...`, `/platform/account/password`.
Platform login has no tenant forgot link.

Base APIs: `/api/account/recovery` and `/api/platform/account/recovery`.
`POST users/{id}` issues; `GET users/{id}` returns last ten secret-free statuses;
`POST {id}/revoke` revokes; `POST complete` consumes; `PUT password` changes own
password. Platform-only `GET users` returns at most 100 maintenance accounts.

Issue payload: `reason`, `deliveryReference`, `identityConfirmed`, platform-only
`currentPassword`. Complete: `token`, `newPassword`. Self-change:
`currentPassword`, `newPassword`. Platform revoke requires `currentPassword`.
Response issue: `id`, `token`, `expiresAt`; all other mutations return 204.

Frontend secrets stay in component memory. Fragment is removed from browser URL
immediately; secrets never enter localStorage, query API URLs or analytics.
Closing/destroying the dialog removes the one-time link. Completion clears forms
and secret after success. Public requests carry no tenant/platform JWT and cannot
clear unrelated sessions on 401. Self-change clears only its own session plane.
Original locally-authored SVGs: `public/hf-one-logo.svg`, `hf-one-mark.svg`; mark
also supplies favicon. No external image or generator; no internal-name refactor.

## Exceptional platform loss

**R4 HUMAN EXECUTION REQUIRED** when no Platform Admin can authenticate.
There is no in-app bootstrap, master password, SQL tool, impersonation, fake JWT
or automated rescue. A human owner must separately authorize an external runbook
with identity evidence, backup/rollback, minimum required intervention and audit.
This document does not authorize execution or provide a credential bypass.

## Automated synthetic validation and preview

Coordinator owns ephemeral infrastructure and runtime timing. Integration tests
use `HF_POS_TEST_CONNECTION_STRING` with an owned `hfpos_test_530` database; the
fixture destroys/recreates it. NEVER share that database with the human preview.
Local evidence uses PostgreSQL 18.1; required GitHub CI uses PostgreSQL 16.
No Fernando Development database update, UserSecrets, real business data, real
SRI, SMTP or signing certificate is used.

Reusable test-only host accepts ONLY `hfpos_test_530_smoke`, loopback and
`hfpos_test` user, validates its exact configuration before initialization and
discards ambient/CLI unsafe overrides. It is not part of the production API or
container. TLS uses only an ephemeral self-signed localhost test certificate.
No fiscal/signing fixture is initialized in assisted recovery mode.
On Windows, Schannel uses a temporary synthetic user key container (UserKeySet,
NOT PersistKeySet), disposed with the host-owned certificate. Other platforms
use EphemeralKeySet. No PFX is written, no certificate is installed, and no
system/user trust is changed. The in-memory PFX bytes are zeroed after import.
See the [runtime Schannel limitation](https://github.com/dotnet/runtime/issues/23749).

Coordinator supplies the connection and runs from repo root:

```powershell
./scripts/ops/start-assisted-recovery-smoke.ps1 -Initialize -Verify
./scripts/ops/start-assisted-recovery-smoke.ps1 -VerifyConfiguration
./scripts/ops/start-assisted-recovery-smoke.ps1 -VerifyListener
./scripts/ops/start-assisted-recovery-smoke.ps1 -Initialize
```

First three commands are automated checks; last starts the isolated API at
`https://localhost:7096`. Restarts WITHOUT `-Initialize` preserve preview data.
HTTP verification uses the existing Production-security in-process test factory.
Only its construction suppresses assembly HostingStartup discovery; the setting
is restored immediately. Preview/configuration still require Testing, retain all
unsafe-override checks, and never permit the synthetic startup in Production.
VerifyListener exclusively opens loopback HTTPS 7096, performs a real handshake
and GET pinned to the exact generated certificate, then stops/disposes the host.
It never connects to or initializes the database and cannot combine Initialize.
Stop the preview first to avoid a port/binary collision. CI exercises this mode
on both Linux and Windows Schannel, not merely in-process WAF/TestServer.
Coordinator serves the existing frontend Development configuration at
`http://localhost:4200` and prepares browser TLS access before human handoff.
No human Docker/SQL/token generation/concurrency/infrastructure chores.

Synthetic credentials only: ADMIN `cashier-recovery530 / old-only`, owner
`owner-530 / old-only`; platform `platform-530-a` and `platform-530-b` /
`synthetic platform password 530`. Short legacy passwords deliberately verify
backward-compatible login; newly set passwords must meet 12..256.

## Human pre-merge acceptance

Pending, not accepted on Fernando's behalf. Coordinator provides a live URL after
backend/PostgreSQL/frontend/build/audit/EF/required exact-HEAD CI and independent
review have passed. Do not request human acceptance before those gates.

1. Open HF One login, inspect logo/layout and the tenant forgot link.
2. Sign in as synthetic ADMIN, select owner-530 in Administration/Users.
3. Enter synthetic identity evidence and generate the link; share/open it.
4. Owner sets and confirms a new password with at least 12 characters.
5. Old password fails; new password signs in normally.

No email delivery/verification, MFA, impersonation, production deployment,
inventory/sales/cash mutation or commercial self-service is in this ticket.

Public tenant login/recovery routes (including trailing slash variants) skip
authenticated startup /me refresh, preserving a valid recovery link even when
the browser has a stale tenant JWT. Private routes still load /me and reject 401.

## Rollback

Rollback is a human-reviewed code release plus disposable-database rehearsal.
Migration Down deletes challenge/audit tables and removes the permission while
invalidating roles that lose it. It loses recovery history, so production rollback
requires a separately approved backup/runbook. Never revert to the legacy reset
as an unreviewed workaround. A code merge is not deployment authorization.
