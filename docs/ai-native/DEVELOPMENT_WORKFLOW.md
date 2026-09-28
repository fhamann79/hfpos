# HFPOS AI-Native Development Workflow

## Purpose

This document defines the operating model for developing HFPOS with AI agents while preserving correctness, auditability and human control over high-risk decisions.

The root `AGENTS.md` is normative. This document explains how to run the process day to day.

## Roles

### Orchestrator

Owns sequencing and context.

Responsibilities:
- reconcile against current `main`;
- convert backlog items into explicit tickets;
- build the dependency graph;
- decide which tickets can run in parallel;
- assign branches/worktrees and file ownership;
- prevent duplicate or conflicting work;
- collect implementation, review and CI evidence;
- never treat CI green as merge authorization.

### Implementer agent

Writes code for exactly one ticket in one branch/worktree.

Responsibilities:
- respect ticket scope and non-goals;
- read root and nested `AGENTS.md` files;
- add/update tests with the change;
- report exact validation results;
- open/update the ticket PR;
- never merge.

### Specialist agent

Read-only unless explicitly promoted to implementer on its own branch.

Examples:
- architecture;
- security/RBAC/tenant isolation;
- PostgreSQL/query/concurrency;
- frontend UX/accessibility;
- migration/model review;
- test-gap analysis.

Specialists are the preferred way to obtain parallelism inside one high-risk ticket.

### Independent reviewer

Must not be the implementation author for the PR being reviewed.

Checks, as applicable:
- acceptance criteria and scope;
- architecture boundaries;
- tenant isolation;
- authentication/authorization;
- transaction/locking/idempotency behavior;
- query correctness/performance/N+1/unbounded reads;
- migration safety/model consistency;
- frontend permissions and error states;
- tests vs claimed behavior;
- regression and rollback risks.

Findings use BLOCKER / MAJOR / MINOR / NIT.

### Human owner (Fernando)

Retains final authority for:
- merge authorization;
- production operations;
- persistent/shared database mutation;
- real SRI operations;
- real certificates/keys/secrets;
- acceptance of business/UX behavior when manual validation is required.

## State machine

```text
DISCOVER
  -> READY
  -> IN_PROGRESS
  -> PR_OPEN
  -> REVIEW
  -> CI_GREEN
  -> HUMAN_VALIDATION (when required)
  -> READY_TO_MERGE
  -> MERGED
  -> RECONCILED
```

A ticket may move backward whenever a review, CI run or human validation finds a defect.

## Phase A — Post-merge reconciliation

Before selecting the next ticket:

1. Confirm current `main` SHA.
2. Confirm the prior PR is actually merged.
3. Inspect current open PRs/issues.
4. Confirm required CI on the new `main`.
5. Re-read the relevant implementation, tests and migrations.
6. Re-evaluate roadmap priorities from the repository as it now exists.

Do not auto-increment ticket numbers from an old plan without this reconciliation.

## Phase B — Ticket preparation

A development ticket must contain:

- problem/outcome;
- base SHA;
- acceptance criteria;
- architecture/invariants;
- dependencies;
- affected areas;
- non-goals;
- risk R0-R4;
- automated validation plan;
- manual validation plan;
- parallelization/ownership decision.

A ticket is not READY if the agent cannot explain how correctness will be demonstrated.

## Phase C — Parallel dispatch

Build a dependency graph before launching writing agents.

Safe parallel example:

```text
DEV-010 (independent)  ---> PR A
BE-520  (independent)  ---> PR B
FE-521  (depends on BE-520 contract) ---> waits or explicitly branches from PR B
```

Rules:
- 2-3 simultaneous writing fronts by default;
- each front uses a separate branch/worktree;
- avoid overlapping changed files across active fronts;
- shared migrations, auth core, DbContext, permission catalogs and cross-cutting contracts are coordination hotspots and should usually be serialized;
- analysis/review specialists may run concurrently without this limit because they are read-only.

## Phase D — Implementation

Branch convention:

```text
codex/<ticket-lowercase>-<short-description>
```

Examples:

```text
codex/dev-001-ai-native-workflow
codex/be-fe-512-cash-close-hardening
```

During implementation:
- do not broaden scope opportunistically;
- if a new defect is unrelated, open a separate ticket;
- if an assumption is disproved by the code, update the ticket/plan before continuing;
- prefer small cohesive commits, but PR correctness matters more than commit count;
- never use real credentials/data to make a test pass.

## Phase E — PR evidence contract

The PR template is not prose decoration. It is the evidence package consumed by reviewers and automation.

It records:
- ticket and base SHA;
- risk;
- invariants;
- scope/non-goals;
- architecture and data/migration effects;
- exact tests and results;
- manual validation;
- independent review status;
- real-environment declaration;
- merge state.

The automated governance workflow checks this minimum contract and computes a conservative minimum risk from modified paths.

## Phase F — Automated validation

Existing CI remains authoritative for product build/test gates:
- `Backend (.NET 8 + PostgreSQL)`;
- `Frontend (Angular 21)`.

DEV-001 adds `AI-Native Governance` for PR metadata/risk-policy validation.

Risk drives additional evidence:

| Risk | Required baseline |
|---|---|
| R0 | scope/diff sanity |
| R1 | targeted tests + affected build/type checks |
| R2 | relevant regression suite + full affected CI + independent review |
| R3 | R2 + DB/concurrency/idempotency/migration/security evidence as applicable + human validation |
| R4 | no autonomous execution; human runbook/rollback/authorization |

## Phase G — Independent review

Review is a distinct task, not a self-review prompt appended to implementation.

The reviewer should receive:
- issue/ticket;
- PR diff;
- relevant `AGENTS.md` files;
- CI results;
- claimed invariants/tests.

The reviewer should actively try to falsify the implementation, especially around hidden state, stale reads, races, tenant boundaries, authorization and SQL behavior.

Corrections go back to the same branch/PR and are re-reviewed.

## Phase H — Human validation and merge

Manual validation is progressive (`UX-1`, `UX-2`, etc.) for user-visible or operationally sensitive work.

Examples:
- visual/interaction correctness;
- business flow expectations not fully encoded in tests;
- device/browser/peripheral behavior;
- confirmation that a risky operational step is appropriate.

Agents do not merge. Fernando authorizes the merge only after required evidence is complete.

## Phase I — Cleanup

After merge:

```bash
git switch main
git pull --ff-only origin main
git branch -d <ticket-branch>
```

For worktrees, remove the ticket worktree after its branch is no longer needed.

Then begin Phase A again from the new `main`.

## Control points for HFPOS

The following areas always deserve deliberate review because failures are disproportionately costly:

- tenant/company/establishment/emission-point boundaries;
- user/role/permission/session semantics;
- sales, purchases, inventory and cash mutations;
- cancellations/voids/credit notes;
- cost/stock recalculation and concurrent writes;
- EF migrations and constraints;
- SRI lifecycle, XML signing and authorization states;
- reports/exports that can diverge from transactional truth;
- pagination/aggregation where page results must not change global summaries.

## What AI should automate aggressively

- repository discovery and impact analysis;
- test generation and regression expansion;
- isolated implementation tickets;
- parallel read-only specialist reviews;
- diff review and risk detection;
- CI/build/test execution;
- PR evidence preparation;
- issue/PR bookkeeping;
- detection of stale branches, dependency conflicts and missing validation.

## What AI should not autonomously execute

- production deployments or destructive changes;
- persistent/shared/production database migrations;
- real SRI submissions;
- use/rotation/export of real certificates, keys or secrets;
- final merge authorization.
