# HFPOS — Repository-wide AI-Native Agent Policy

This file is the root policy for every AI agent and human contributor working in this monorepo.
Nested `AGENTS.md` files may add stricter area-specific rules, but they MUST NOT weaken this policy.

## 1. Source of truth

- `main` is the only repository source of truth.
- Before starting a ticket, verify the current `main` SHA, open PRs/issues, relevant CI state, and the code that actually exists.
- Never infer the next implementation only from an old roadmap, chat, prompt, or previous branch.
- One ticket = one branch = one PR unless the ticket explicitly defines a dependency chain.
- Corrections discovered during review stay in the same ticket branch/PR.

## 2. Mandatory lifecycle

Every change follows this order:

1. **Reconcile**: inspect current `main` and repository state.
2. **Propose and approve**: propose a coherent development group from real product gaps; wait for Fernando's approval before creating tickets or starting a new functional group. Explicit approval of a scoped standalone ticket also satisfies this gate.
3. **Ticket and plan**: materialize only approved tickets with acceptance criteria, dependencies, ownership, invariants, non-goals, risk and validation plan.
4. **Implement**: use an isolated branch/worktree; keep scope limited to the ticket.
5. **Self-check**: run the tests/checks appropriate to the declared risk.
6. **PR**: provide evidence using the repository PR template. Implementers do not merge.
7. **Independent review**: a reviewer that did not author the implementation inspects the diff, architecture, tenant isolation, concurrency, SQL/data behavior, security and tests as applicable.
8. **CI**: all required automated checks must be green.
9. **Human gate**: complete merge-blocking human validation; explicitly schedule eligible consolidated cycle validation under section 4.
10. **Conditional merge**: the coordinator may merge an approved ticket only after every condition below is evidenced; otherwise stop at the applicable human gate.
11. **Post-merge**: verify GitHub merge and new `main` SHA/CI, reconcile remaining group PRs and validate their effective diffs proportionally on exact new HEADs. Continue the integration loop automatically; no `MERGEADO` prompt is needed.
12. **Cycle closure**: verify final `main` and integration, obtain required cycle acceptance, clean only obsolete resources that no remaining front needs, then return to DISCOVER and propose the next group. Do not start it without approval.

A green CI run is necessary but never sufficient to merge. Authority comes from Fernando's approved scope and the policy already integrated in `main`, not a proposed policy in a ticket branch. A PR changing merge authority follows the policy on its base `main`; it MUST NOT grant itself new authority. The PR introducing conditional merge therefore ends at **LISTO. HAZ MERGE MANUAL #<PR>**. Only after Fernando's merge is verified in `main` does this delegation apply to later cycles.

When acting as orchestrator, Codex owns discovery, proposal, internal ticket prompts/delegation, dependency and collision analysis, validation, independent review, CI and the entire merge loop. Fernando approves **what**; Codex coordinates **how**, without asking Fernando to copy prompts between agents. The coordinator is not the default feature implementer or a silent additional writer; an approved operating-policy update may be authored by the coordinator itself.

Human gates are scope approval, ambiguous functional/material decisions, required pre-merge or cycle validation, and separately authorized R4 operations. Do not ask for ceremonial merges when conditional authority applies. Stop only the affected front on a material conflict; independent fronts may continue. Report unrelated findings for later prioritization, not opportunistic fixes or unapproved tickets.

### Conditional merge gate

The coordinator, never the implementer acting as its own reviewer, may execute a merge only when ALL are true:

- Fernando approved the ticket's actual scope; no scope expansion or functional decision remains pending.
- The final published HEAD has required CI green and independent review with **BLOCKER=0 / MAJOR=0**; review/evidence covers that exact effective change and its current integration base.
- There is no material conflict, unexplained interaction or unresolved doubt, and any required **pre-merge** human validation has been accepted by Fernando for the relevant change.
- The merge crosses no pending critical safety barrier; eligible consolidated cycle smoke is explicitly justified by coordinator and independent reviewer, scheduled and still recorded as pending acceptance.
- It is not R4 and uses no real business data, production infrastructure operations, secrets, certificates or real SRI. A code merge is not deployment authorization.
- Immediately before merge, recheck HEAD/base and gates. Bind the merge request to the verified HEAD and honor GitHub required checks/rules (including merge-queue checks when configured). Never bypass protections, use an admin override or arm unattended auto-merge before gates are complete. If safe execution cannot be demonstrated, stop.

For critical semantics or material doubt, say **VALIDACION HUMANA PRE-MERGE REQUERIDA**, give concrete steps and wait for **VALIDADO OK**. Then recheck all remaining conditions; that reply alone is not sufficient. Explicit human restrictions such as `NO MERGE` always take precedence. R4 remains human-controlled, never autonomous.

## 3. Parallel agents and worktrees

The default is to parallelize independent work, not shared mutable work.

- Each implementation agent gets its own ticket, branch and worktree.
- Two writing agents MUST NOT work in the same worktree.
- Two writing agents MUST NOT edit overlapping files unless the ticket explicitly defines ownership boundaries and an integration order.
- Analysis/research/review agents may run in parallel because they are read-only.
- For one large ticket, prefer one writing implementer plus parallel read-only specialists (security, SQL/concurrency, frontend UX, tests) rather than multiple writers on the same files.
- Parallel tickets must declare dependencies. If ticket B depends on unmerged ticket A, B either waits or branches from A and declares the dependency explicitly; it must not pretend to be independently based on `main`.
- The orchestrator is responsible for detecting conflicting paths, duplicated work and stale bases before dispatching agents.

Usual active writing range: **2–3**, not a quota. The dependency graph and collision analysis determine the safe number; use one implementer for tightly coupled work.

## 4. Risk matrix

Declare one level in every development ticket and PR. The automated governance check may raise the minimum level based on modified paths.

### Proportional fast path

Validation follows real risk and the effective diff: maximize safety per unit of time/token, not check count. **Do not repeat a demonstrated test/review when its relevant code and integration assumptions have not changed.** Reuse prior evidence with its original SHA/scope and explain equivalence; never claim old CI as CI for a new HEAD. Required CI must still be green on the final published HEAD, without disabling required jobs.

- During implementation, use focused tests for feedback. A full relevant suite already run by final CI need not also run locally unless needed for diagnosis or a concrete risk.
- Frontend-only: relevant frontend tests, build, runtime audit when applicable and CI. No local backend/PostgreSQL/EF/recovery unless a real dependency or interaction justifies it.
- Backend read-only/reporting/export: pertinent backend tests and CI; no full frontend suite unless a shared contract/interaction changes.
- PostgreSQL, EF/model/migrations, concurrency and Containers/recovery are required when the effective persistence, integration or operational risk warrants them, not by ceremony. Automatically required CI is never waived.
- Critical money/transactions, mutable stock, locking/idempotency, auth/RBAC/session, tenancy, migrations/data repair, SRI lifecycle and privileged operations retain deep proportional validation and pre-merge human barriers.
- Keep conservative path-derived R3 and required human validation, but justify read-only/output/UX fast paths from the effective diff; a path alone does not mandate a full transactional audit. See the timing rules below.
- Batch BLOCKER/MAJOR corrections. Review a focused correction's delta plus integration impact; do not restart an already demonstrated full functional review or review the review without new material risk.

### R0 — documentation / non-executable metadata

Examples: explanatory docs with no workflow/runtime effect.

Minimum validation:
- diff/scope review;
- syntax/link sanity when relevant.

### R1 — low-risk isolated behavior

Examples: isolated UI presentation, tests, developer tooling that cannot change acceptance/production behavior.

Minimum validation:
- targeted automated tests;
- build/lint/type checks for the affected area;
- manual UX check when visual behavior changed.

### R2 — application or CI behavior

Examples: API/application behavior, frontend business flows, CI/governance, reporting behavior without critical transactional state changes.

Minimum validation:
- affected automated tests plus regression coverage;
- full relevant build/test job;
- independent review;
- manual functional validation when user-visible behavior changed.

### R3 — critical transactional/security/data behavior

Examples: migrations/model changes, authentication/authorization, tenant isolation, money, inventory, purchases, sales, cash, credit notes, concurrency/locking, transaction boundaries, SRI document lifecycle.

Minimum validation:
- focused unit/integration tests and regression tests;
- PostgreSQL-backed tests when persistence semantics matter;
- concurrency/idempotency tests when applicable;
- migration/model verification when applicable;
- independent architecture/security/data review;
- human validation with timing determined by the rules below; critical semantics require it before merge.

### Human validation timing

- **Merge-blocking**: validate before merge when changes actually affect critical semantics: money/transactions, stock/purchase/sale/cash mutations, concurrency/locking/idempotency, auth/RBAC/session, tenant isolation, migrations/data repair, SRI lifecycle or privileged operations. Consolidation must not defer this safety barrier.
- **Consolidated cycle validation**: compatible UX, read-only queries, pagination, search, reporting/export and non-destructive interactions may share one smoke, including after the individual merges, only when technical tests, independent review and exact-HEAD CI are sufficient. The coordinator and independent reviewer must explicitly justify in the PR why no merge-blocking human barrier remains, list scope/steps and record Fernando's pending cycle acceptance. The category alone is not an exemption; security or critical semantics still use pre-merge validation.
- Keep the declared/path-derived risk unchanged. A conservative R3 caused by paths may use consolidated smoke only if the effective diff changes no critical semantics and the above justification is evidenced. R3 still declares human validation required; it does not become R2 to avoid checks.
- If there is material doubt, use pre-merge validation. A controlled temporary integration of compatible HEADs may support one consolidated **pre-merge** smoke; it is not a merge to `main` or authorization for R4.

### R4 — real-world destructive or privileged operation

Examples: production deployment/change, real database mutation, real SRI submission, real certificates/keys, secret rotation, irreversible data repair.

Rules:
- autonomous agents MUST NOT execute R4 operations;
- real credentials, certificates, production data and real SRI endpoints MUST NOT be exposed to agent sandboxes;
- an R4 task requires a separate human-approved runbook, backup/rollback plan and explicit execution authorization.

## 5. Non-negotiable HFPOS invariants

Whenever relevant, reviewers and implementers must explicitly reason about these invariants:

- tenant isolation by `CompanyId` and the operational establishment/emission-point context;
- authorization is enforced server-side, never only in the UI;
- controllers stay thin and business logic remains in the backend service/core boundaries defined by the nested backend policy;
- transaction, locking and idempotency semantics protect money, stock, purchases, sales, cash and electronic documents;
- SQL/query behavior must be evaluated for correctness and avoid accidental N+1 or unbounded reads;
- migrations and the EF model remain consistent;
- user-visible frontend flows preserve strict typing and permission behavior;
- no secret, certificate, real taxpayer data or production connection string is committed.

## 6. Databases, migrations, SRI and secrets

- Agents MUST NOT run `dotnet ef database update` against Fernando's persistent Development database, any shared database or production.
- Migration validation may use disposable/ephemeral databases created specifically for tests/CI.
- Never call real SRI services from automated tests or agent sandboxes.
- Never use real signing certificates or private keys in tests, prompts, commits, logs or artifacts.
- Use synthetic test data only.
- Never commit secrets. Test-only credentials must be obviously non-production and scoped to ephemeral CI/test environments.

## 7. Pull request discipline

Every PR must:

- use a ticket-prefixed title such as `DEV-001`, `BE-FE-512`, `BE-513`, or `FE-514`;
- identify the issue/ticket and base SHA used to start the work;
- declare risk level and protected invariants;
- state scope and non-goals;
- list migrations and contract/API changes explicitly;
- report exact tests/checks executed and their result;
- state what requires manual validation, its pre-merge or consolidated timing, justification and pending human acceptance; evidence conditional merge eligibility or the remaining human gate;
- state whether real environments/data/SRI/certificates were used (the expected answer for autonomous work is `NO`);
- record independent-review findings/blockers;
- finish with an explicit merge state. AI-authored PRs start as `PENDIENTE`.

Agents MUST NOT invent Fernando's approval/acceptance or mark `AUTORIZADO_POR_FERNANDO` on his behalf. Conditional execution is delegated by this policy plus approved scope and evidenced gates, not an editable PR-body approval flag. Keep `PENDIENTE` until actual integration, then record the real merge and acceptance state.

## 8. Review severity

Independent reviewers classify findings as:

- **BLOCKER**: data loss/corruption, tenant leak, authorization bypass, wrong money/stock/cash semantics, unsafe migration, real secret exposure, broken required CI, or another correctness/security issue that forbids merge.
- **MAJOR**: important defect or regression risk requiring correction before merge.
- **MINOR**: non-blocking quality/maintainability improvement.
- **NIT**: optional style/detail.

Only BLOCKER and MAJOR findings block readiness, unless the ticket defines a stricter bar.

## 9. Readiness and cycle acceptance

- **PR READY TO MERGE**: acceptance criteria have technical evidence; required tests/builds and independent review are complete; evidence covers the final effective change and required CI is green on its exact published HEAD; no unresolved BLOCKER/MAJOR remains; merge-blocking human validation is complete. Any eligible consolidated validation is explicitly justified and scheduled. The coordinator merges only if the conditional gate is fully satisfied.
- **MERGED — AWAITING CYCLE ACCEPTANCE**: the PR is integrated, but its declared consolidated human validation remains pending. Do not call the ticket/cycle DONE.
- **CYCLE FUNCTIONALLY ACCEPTED**: final `main`, integrated PRs and required CI are verified, all required human validation is complete and Fernando has accepted the cycle (for example, `VALIDADO OK`). If no human smoke applies, record why, the approved scope and actual integration evidence; do not invent a smoke result or request acceptance that adds no information.
- Cleanup is safe only for merged, integrated, clean obsolete branches/worktrees and owned test resources that no remaining front depends on. Preserve uncommitted/preexisting local work. Cleanup is not permission to discard data. After closure, return to DISCOVER; proposing the next group does not authorize implementation.

## 10. Area policies

Before editing an area, also read its nested policy:

- Backend: `backend/Pos.Backend.Api/Pos.Backend.Api/AGENTS.md`
- Frontend: `frontend/pos-frontend/AGENTS.md`

When rules conflict, the safer/stricter rule wins.
