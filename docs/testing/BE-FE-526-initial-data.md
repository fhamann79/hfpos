# BE-FE-526: Initial business data and tenant setup

Issue #128; approved roadmap #127. Base: `19a84cec9ded6c771816d3f20b021500d3a3370d`.
Risk R3. No real data, secrets, certificates, SRI calls or persistent database used.
Human acceptance and independent review are pending. Implementation is not merge authorization.

## API and CSV contract

All endpoints require the current authenticated operational context. Clients cannot choose CompanyId,
actor, establishment or emission point in the import payload. Frontend route: `/initial-data`.

| Endpoint | Contract |
| --- | --- |
| `GET /api/initial-data/templates/{kind}` | Exact CSV header; existing read/write permissions for that dataset |
| `POST /api/initial-data/preview` | `{requestId: UUID, kind, csv, duplicatePolicy: "create-only"}` |
| `POST /api/initial-data/confirm` | `{payload: same preview request, previewToken}` |
| `GET /api/initial-data/batches?page=1` | Company-scoped committed results, newest first, 30 per page; OP_STRUCTURE_READ |
| `GET /api/initial-data/readiness` | Safe metadata checks and existing page routes; OP_STRUCTURE_READ, FISCAL_SETTINGS_READ, ADMIN_USERS_READ |

Preview returns `requestId`, `kind`, `duplicatePolicy`, `atomicity: "atomic-batch"`, `maxRows: 500`,
`canConfirm`, optional protected `previewToken`, file errors and rows containing physical `rowNumber`,
normalized `values`, row errors and optional resolved tenant-local ID. Preview does not write records.
Confirm returns batch/request IDs, kind, company/establishment/point/actor IDs, row count,
created IDs and their corresponding CSV row numbers, and creation time.

CSV: comma-delimited UTF-8; BOM, quoted fields and CRLF/LF accepted. Exact ordered headers:

```csv
name
internalCode,barcode,name,category,price,cost,minimumStock,vatCategory
name,identificationType,identification,phone,email,address,notes
name,identification,phone,email,address,notes
internalCode,quantity
```

These correspond respectively to `categories`, `products`, `customers`, `suppliers`, `opening-inventory`.
Maximum 500 rows and 1 MiB UTF-8 CSV; HTTP envelope maximum 2,200,000 bytes. Fields are trimmed;
field lengths, decimals, VAT enum names and customer identification types are validated per row.
Prices accept up to 2 fractional digits; cost, minimum stock and quantities accept up to 4.
Numbers are nonnegative invariant decimals, without thousands separators. No implicit price, cost or VAT defaults.
Category references use exact active names; products require an internal code and optional barcode.
Customer/supplier imports require a stable identification; optional-identity manual workflows stay unchanged.

Existing masters, including inactive ones, cause `DUPLICATE_CREATE_ONLY`; no update, skip or merge by name/email.
Category duplicate keys are case-normalized; other identifiers retain repository case-sensitive semantics.
Opening inventory is only for active products with zero stock and no movement history at that establishment.
Zero balance after previous activity is NOT an unused opening balance.

Preview proof expires after two hours and binds schema/material hash, UUID, actor/company/context and resolved IDs.
Hash uses parsed trimmed cells in header order, ignoring BOM, line endings and physical row numbers;
numeric textual changes such as `10` to `10.0` are still different material. A new intent needs a new UUID.
Confirm revalidates current permissions, session/role versions, references, duplicates and stock under Company lock.
A committed same-actor/context/material replay returns the durable result before proof expiry or duplicate checks;
the HTTP client retains its original token for retry. Changed material/context/actor gives HTTP 409
`INITIAL_DATA_REQUEST_CONFLICT`; invalid proof gives 400; current-state conflicts give 409
`INITIAL_DATA_REVALIDATION_FAILED`. Existing auth/context errors keep their 401/403 semantics.

## Inventory and identity contracts

`GET /api/Inventory/products/{id}/stock` adds `movementWatermark` to quantity and context IDs.
`POST /api/Inventory/adjust` now requires `expectedMovementWatermark`, `expectedQuantity`,
`expectedCompanyId`, `expectedEstablishmentId` alongside the existing target `quantity`.
Missing snapshot gives 400 `INVENTORY_SNAPSHOT_REQUIRED`; changed stock/context/watermark gives
409 `INVENTORY_SNAPSHOT_STALE`. A 10 -> 8 -> 10 movement sequence invalidates the original snapshot.
Opening uses InventoryService adjustment-to-target semantics and source type 11 `OpeningInventory`,
with source batch/row IDs. No new direct SQL stock writes. Ledger, domain movements and master cost events
share one transaction. Failure of any row rolls back the entire batch.

Platform provisioning adds initial establishment/point `code` (001 defaults) and requires a real establishment
address for new tenants, also initializing the company matrix address. Legacy committed provisioning replay
with missing codes/N/A remains compatible; passwords remain outside snapshots.
Establishment read/create/update adds code/address; update omission preserves current values.
Codes are exactly three ASCII digits except 000, unique including inactive structure. Point codes follow the same rule.
Used identity is protected by sales/credit notes (including cancelled) or used/audited document sequences:
changing used establishment/point codes or company RUC returns 409. Legitimate name/address edits remain allowed.
Existing document numbers, historical snapshots and XML are not rewritten or renumbered.

Readiness is metadata, not SRI authorization or a certificate cryptographic check. It never creates settings
or loads certificate contents. Users, contexts, last-admin protection, permissions and session versions reuse
the existing services; context choices display code plus name and ignore superseded point responses.

## Persistence and lock boundary

Migration: `20261004050131_AddInitialDataBatches` plus Designer and model snapshot.
Additive `InitialDataBatches` table only: unique CompanyId/RequestId, actor/context restrictive FKs,
bounded kind/hash, row count, timestamp and durable result JSON. No CSV/customer PII stored in ledger.
CreatedIds/RowNumbers and movement SourceId/SourceLineId provide row-level audit linkage.

Confirm acquires Company FOR UPDATE before revalidation/mutation; preview uses the existing shared Company
guard and a single MVCC projection for opening quantity/watermark. Manual category/customer writers join
the Company locking protocol; existing product writers already use it. Supplier create/update/delete now
join Company-first locking and validate operational/session context after waiting, before supplier reads. Counts/opening acquire
Company exclusive before stock lookup/creation, including absent stock rows. Broader inventory changes remain out of scope.

Do not downgrade the migration by dropping committed audit history. Revert application code first if needed;
any real data repair, database mutation or deployment requires a separate human-authorized runbook.

## Executed verification

All local commands ran in the isolated 526 worktree. Backend environment was explicitly Testing,
SeedDemoData=false; tests used only owned `postgres:16` container `hfpos-be-fe-526-tests`, database
`hfpos_test_526` with synthetic test-only credentials and no persistent volume.

- `dotnet build backend/Pos.Backend.Api/Pos.Backend.Api.sln --configuration Release -clp:ErrorsOnly`: success, 0 errors; warnings remain.
- `dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.Tests --configuration Release --no-build --filter 'FullyQualifiedName~InitialDataIntegrationTests|FullyQualifiedName~PlatformControlPlaneTests|FullyQualifiedName~MasterData|FullyQualifiedName~FiscalSettings|FullyQualifiedName~InventoryTransferIntegrationTests' --logger 'console;verbosity=minimal'`: 61/61 pass before final small delta.
- `dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.Tests --configuration Release --no-build --filter 'FullyQualifiedName~InitialDataIntegrationTests' --logger 'console;verbosity=minimal'`: final backend delta 17/17 pass.
- `dotnet ef migrations has-pending-model-changes --project backend/Pos.Backend.Api/Pos.Backend.Api --startup-project backend/Pos.Backend.Api/Pos.Backend.Api --configuration Release --no-build`: no pending model changes. Fixture migrations applied on owned disposable PostgreSQL.
- `npm ci --no-audit --no-fund`: success.
- `npm test -- --watch=false --include='src/app/features/initial-data/*.spec.ts' --include='src/app/features/inventory/pages/inventory-page/inventory-page.spec.ts' --include='src/app/features/operational-structure/**/*.spec.ts' --include='src/app/modules/platform/platform-tenants.spec.ts'`: 116/116 pass.
- `npm test -- --watch=false --include='src/app/features/administration/components/users/user-dialog.spec.ts'`: final selector delta 4/4 pass; no RBAC contract changes.
- `npm run build`: success, including 3 production environment/bundle security checks.
- `npm audit --omit=dev --audit-level=high`: 0 vulnerabilities.
- `git -c core.safecrlf=false diff --check`: success.

Integration tests cover all datasets, no-write preview, limits/CSV syntax, replay/concurrent duplicate UUID,
changed payload, permissions/foreign context, revocation while waiting for Company lock, manual create versus import,
second-row rollback, absent stock concurrency, opening balance reverting to zero, count balance reverting to original,
manual-cost events, identity protection and legacy provisioning replay. Required CI must cover the exact published HEAD.

## Consolidated review correction (after ebaba32)

Carson's independent baseline review: BLOCKER=0 / MAJOR=3. This batch addresses the three findings;
it is not reviewer acceptance. Keep the corrected HEAD frozen for delta plus integration review.

- MinimumStock: `.HasSentinel(3m)` preserves the legitimate CLR/database default 3 and sends explicit 0
  on insert. Snapshot updated for runtime metadata; no schema/default change and no new SQL migration.
  New PostgreSQL regression checks actual persisted import values 0/3 and the unchanged default 3.
- Suppliers: manual Create, Update (including activation/identification changes), Delete now acquire
  Company FOR UPDATE before supplier reads/writes, then use the existing guard for current context/session
  validation. No supplier row lock is acquired first; existing centralized 23505-to-409 remains unchanged.
  Four deterministic PG races cover create/update, both manual-first and import-first. The first writer is
  held after acquiring Company lock; `pg_blocking_pids` proves the second is actually blocked by that owner
  before release. Assertions cover conflict, one natural key, rollback of losing update and ledger count.
  A separate test revokes the manual supplier session while it waits and asserts no write.
- Audit UI: `rowNumbers` is now required in the typed result contract. Applied result and expandable history
  show CSV physical row -> created ID, or movement ID for opening inventory; bounded scrolling preserves layout.
  Component DOM test verifies nonconsecutive rows 2/5 against IDs 8/19 in both result and history.
- External ambiguous exact category duplicate finding r4176370428 is rejected by the existing unique,
  unfiltered `(CompanyId, Name)` index: PosDbContext and migration
  `20260226090000_AddCompanyScopeToCatalogAndOperationalContext.cs:68`. Case-distinct names do not make
  the exact reference lookup ambiguous. No functional lookup change; PG regression asserts SQLSTATE23505
  / IX_Categories_CompanyId_Name even when the original category is inactive.

Local delta checks: backend Release build succeeds; EF `has-pending-model-changes` reports no changes;
`npm test -- --watch=false --include='src/app/features/initial-data/*.spec.ts'` passes 7/7;
`npm run build` succeeds with 3 environment/bundle checks; diff-check succeeds.
Docker Linux engine remains offline (named pipe missing), so **new PG tests were not run locally**.
No fallback database, degraded Testing, persistent data or engine restart used. PG proof and exact corrected-HEAD
full CI results must be recorded in PR #140 before considering technical evidence complete. Earlier 335/313 CI
belongs only to ebaba32; it is not evidence for a new HEAD. Human R3 smoke and independent delta review remain pending.

## One integrated synthetic pre-merge smoke

**VALIDACION HUMANA PRE-MERGE REQUERIDA.** Pending Fernando's `VALIDADO OK`; not executed by implementer.
Use only a disposable synthetic environment on the final PR HEAD. No real taxpayer, certificate, SRI or Development database.

1. Provision a synthetic tenant with establishment 017, point 009, synthetic address and test-only admin.
   Verify selected context, matrix address, codes, duplicate rejection and existing admin/role/context setup.
2. Open Preparacion del negocio. Follow existing fiscal/users/structure links; verify safe pending readiness
   without uploading a certificate or enabling real SRI. Create a second synthetic user with the existing service;
   rapidly change establishment choices and verify code/name labels and only the latest points.
3. Download all five CSV templates. Preview a malformed/duplicate row and confirm that no masters, batches or stock change.
   Load one category, a product with explicit price/cost/VAT, one synthetic customer and supplier in separate valid batches.
   Confirm each; retry one identical UUID/payload and verify identical result and no second records. Change payload with
   the committed UUID and verify conflict. Observe actor/context, created IDs and row audit in batch results/history.
4. Preview/confirm opening quantity 10 for that unused product. Verify domain movement source OpeningInventory,
   balance 10 and batch/row linkage. Retry the same batch; no second movement. New opening intent after activity must fail.
5. In two sessions, obtain a count snapshot then register exit 2 and entry 2 in the other session. Submit the old target count:
   expect 409 despite balance returning to 10, no extra movement and unchanged stock. Refresh snapshot and submit a valid
   target count; verify a domain adjustment and the new balance. Wrong context/permissions must not expose or mutate another tenant.
6. Configure a synthetic document sequence in existing fiscal settings with an audited next number. Attempt to change used
   codes/RUC: expect 409. Edit name/address: expect success with sequence, historical snapshots and numbers unchanged.
7. Return to readiness/history; verify completed data checks and safely pending fiscal metadata, pagination and error states.
   Record exact HEAD, observations and `VALIDADO OK` or defects in the PR. Independent review and exact-HEAD CI remain separate gates.

For the corrected HEAD, include explicit minimum stock 0 in step 3 and inspect the saved value;
also expand history and verify CSV physical rows against created/master movement IDs. No real data is needed.
