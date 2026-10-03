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
6. **PR**: provide evidence using the repository PR template. Do not merge.
7. **Independent review**: a reviewer that did not author the implementation inspects the diff, architecture, tenant isolation, concurrency, SQL/data behavior, security and tests as applicable.
8. **CI**: all required automated checks must be green.
9. **Human gate**: complete merge-blocking human validation; explicitly schedule eligible consolidated cycle validation under section 4.
10. **Merge**: only Fernando authorizes the final merge after blockers are resolved. AI agents MUST NOT merge.
11. **Post-merge**: verify GitHub merge and new `main` SHA/CI, reconcile remaining group PRs, rerun relevant tests/review/CI on their exact new HEADs and recommend the next manual merge. Continue automatically to the next human gate; no new technical prompt is needed.
12. **Cycle closure**: verify final `main` and integration, obtain required cycle acceptance, clean only obsolete resources that no remaining front needs, then return to DISCOVER and propose the next group. Do not start it without approval.

A green CI run is necessary but is never, by itself, authorization to merge.

When acting as orchestrator, Codex owns discovery, proposal, internal ticket prompts/delegation, dependency and collision analysis, validation, independent review, CI and the entire merge loop. Fernando approves **what**; Codex coordinates **how**, without asking Fernando to copy prompts between agents. The coordinator is not the default feature implementer or a silent additional writer; an approved operating-policy update may be authored by the coordinator itself.

Human gates are group approval, ambiguous functional/material integration decisions, required human validation, each manual merge and separately authorized R4 operations. Stop only the affected front on a material conflict; independent fronts may continue. Report unrelated findings for later prioritization, not opportunistic fixes or unapproved tickets.

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
- state what requires manual validation, its pre-merge or consolidated timing, justification and pending human acceptance;
- state whether real environments/data/SRI/certificates were used (the expected answer for autonomous work is `NO`);
- record independent-review findings/blockers;
- finish with an explicit merge state. AI-authored PRs start as `PENDIENTE`.

Agents MUST NOT change a PR to a human-authorized merge state on Fernando's behalf.

## 8. Review severity

Independent reviewers classify findings as:

- **BLOCKER**: data loss/corruption, tenant leak, authorization bypass, wrong money/stock/cash semantics, unsafe migration, real secret exposure, broken required CI, or another correctness/security issue that forbids merge.
- **MAJOR**: important defect or regression risk requiring correction before merge.
- **MINOR**: non-blocking quality/maintainability improvement.
- **NIT**: optional style/detail.

Only BLOCKER and MAJOR findings block readiness, unless the ticket defines a stricter bar.

## 9. Readiness and cycle acceptance

- **PR READY TO MERGE**: acceptance criteria have technical evidence; required tests/builds and independent review are complete; exact published/tested/reviewed HEAD and required CI agree; no unresolved BLOCKER/MAJOR remains; merge-blocking human validation is complete. Any eligible consolidated validation is explicitly justified and scheduled. Readiness is a recommendation, never agent merge authority.
- **MERGED — AWAITING CYCLE ACCEPTANCE**: Fernando merged the PR, but its declared consolidated human validation remains pending. Do not call the ticket/cycle DONE.
- **CYCLE FUNCTIONALLY ACCEPTED**: final `main`, integrated PRs and required CI are verified, all required human validation is complete and Fernando has accepted the cycle (for example, `VALIDADO OK`). If no human smoke applies, record that justification and the human merge decision; do not invent a smoke result.
- Cleanup is safe only for merged, integrated, clean obsolete branches/worktrees and owned test resources that no remaining front depends on. Preserve uncommitted/preexisting local work. Cleanup is not permission to discard data. After closure, return to DISCOVER; proposing the next group does not authorize implementation.

## 10. Area policies

Before editing an area, also read its nested policy:

- Backend: `backend/Pos.Backend.Api/Pos.Backend.Api/AGENTS.md`
- Frontend: `frontend/pos-frontend/AGENTS.md`

When rules conflict, the safer/stricter rule wins.
