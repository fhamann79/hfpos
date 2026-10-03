# HFPOS AI-Native Development Workflow

The root [AGENTS.md](../../AGENTS.md) is normative. This document describes the daily coordinated delivery cycle, not a new automation framework. Backend/frontend area policies apply to their code. Persist stable rules, not current SHAs, agent names, test counts or ticket history.

## Roles and human gates

- **Orchestrator**: owns discovery, proposals, approved tickets, dependency graph/collision matrix, internal prompts, isolation, validation, review, exact-HEAD CI and the full integration loop. Not the default feature writer; may author an approved operating-policy update.
- **Implementer**: one approved ticket, branch, isolated worktree and PR; implement, test, self-check, commit, push, open a non-draft PR and report exact evidence. Never merge.
- **Specialist/reviewer**: read-only unless separately assigned approved implementation ownership. The independent reviewer must not be the implementation author.
- **Fernando**: approves what to develop, resolves ambiguous functional/material decisions, accepts product behavior and performs every merge. R4 requires a separate human-approved runbook and explicit authorization, never autonomous agent execution.

Fernando must not copy prompts or act as a message bus between agents. Agents own technical builds/tests/audits/EF checks when relevant; human validation focuses on actual business/UX/safety value. Pause at group approval, material decisions, required human validation and each manual merge, not for routine technical coordination.

## Cycle state machine

```text
DISCOVER -> PROPOSE_GROUP -> HUMAN_APPROVAL -> TICKETS_READY
  -> DISPATCH -> IMPLEMENTATION -> REVIEW -> CI_GREEN
  -> MERGE_LOOP -> FINAL_MAIN_VERIFICATION
  -> HUMAN_CYCLE_VALIDATION (when required) -> CYCLE_ACCEPTED -> DISCOVER
```

Merge-blocking human validation occurs inside MERGE_LOOP **before** each affected merge recommendation. A merged ticket with deferred, eligible cycle smoke is `MERGED - AWAITING CYCLE ACCEPTANCE`, not DONE. Findings may return any front to implementation/review; material conflicts pause only that front.

## 1. Discover and propose

Verify current `origin/main` SHA, open issues/PRs, merged work and main CI. Inspect real code, tests, relevant TODOs, security/operation/scalability/UX gaps and known deferred findings. Compare issues to main: pending, partial, superseded, stale, duplicate or no longer relevant. An open issue is not proof of missing functionality.

Propose a compact **NEXT DEVELOPMENT GROUP**:

- base SHA and why now;
- each candidate's outcome, category (bug/security, functional, scalability, operation, technical debt or optional), risk, dependencies and parallelizability;
- ownership/collision risks and integration order;
- deferred findings and why deferred.

Stop for Fernando's approval. Do not auto-increment ticket numbers, create issues or start functional development from the proposal alone. Explicit approval of one scoped ticket is also sufficient; partial approval authorizes only those items. Do not add a coordinator issue merely for ceremony.

## 2. Materialize and delegate approved work

Create/update only the required approved issues using the [ticket template](../../.github/ISSUE_TEMPLATE/dev-ticket.yml). Include outcome, base, acceptance, invariants, scope/non-goals, risk, dependencies, ownership, automated and human validation plans.

Generate and send each subagent's prompt internally: global/area rules plus ticket, base, branch/worktree, exclusive paths, acceptance, relevant code/invariants, prohibitions, checks and result format. Do not forward the entire project chat. Keep current main/cycle, agents, PR HEADs, dependencies, merge order, blockers, deferred findings and pending human decisions clear using the conversation and existing issues/PRs; no state ledger is required.

Dependency graph and collision analysis determine dispatch, not a quota:

- Usually 2-3 writing fronts for independent tickets, each in its own worktree/branch/PR.
- Never two writers in one worktree; avoid shared mutable paths. Explicitly assign ownership/integration order if overlap is unavoidable.
- Dependent tickets wait or explicitly branch from their unmerged dependency; never pretend to be independently based on main.
- One tightly coupled critical change: one implementer plus read-only specialists.
- Shared migrations, auth, DbContext, permissions and cross-cutting contracts usually require serialization.

Observe without constant interruptions. Intervene for contradiction, drift, new dependency/risk, material conflict or genuine blocker. The coordinator does not become a silent additional feature writer.

## 3. Implement, validate and review

Use `codex/<ticket-lowercase>-<short-description>`, keep scope and preserve preexisting local work. Unrelated findings go to the coordinator's deferred list for future prioritization, not automatic fixes or unapproved new tickets.

Each implementer provides a non-draft PR using the [PR template](../../.github/pull_request_template.md): issue/base, risk, invariants, contracts/migrations, exact checks/results, human-validation timing, review, residual risks and merge state `PENDIENTE`. Real data/environments/SRI/certificates/secrets: NO.

Validation is proportional to effective behavior and the root risk matrix:

| Risk | Evidence |
|---|---|
| R0 | scope/diff and relevant syntax/link checks |
| R1 | targeted tests and affected build/type checks; UX check when relevant |
| R2 | relevant regression/full affected checks and independent review |
| R3 | R2 plus PostgreSQL, concurrency/idempotency, model/migration and security evidence as applicable; human validation with timing per root policy |
| R4 | no autonomous execution; separate human runbook/authorization |

Use existing CI, Governance and Containers/recovery checks as applicable; wait for required checks on the **exact published HEAD**. Do not rerun unrelated huge local suites for Markdown-only work, but do not bypass automatically required CI. Local persistence tests use only owned disposable databases with synthetic data; never Fernando's persistent DB, real certificates or SRI.

Organize independent review under [CODE_REVIEW.md](CODE_REVIEW.md). Focus on BLOCKER/MAJOR; batch corrections in the same branch/PR and revalidate changed HEADs. NIT/MINOR do not create endless loops. PR-body claims alone are not independent-review evidence.

## 4. Human validation and merge loop

Apply the root policy's distinction explicitly in every affected PR:

- Critical semantics (money/stock/purchase/sale/cash mutations, locking/idempotency, auth/session/tenancy, migrations, SRI lifecycle or privileged operation): human safety validation is **pre-merge**; do not defer it for convenience.
- Compatible read/query/UX/search/pagination/reporting/export work may share one consolidated smoke, including after individual merges, only with coordinator and independent-reviewer justification that tests/review/CI cover technical risk and no merge-blocking barrier remains.
- Do not lower path-derived R3. Explain conservative path risk versus effective read-only behavior; declare required human validation and its timing. Category alone never exempts security/critical changes. Material doubt means pre-merge.
- A temporary, controlled integration of frozen compatible HEADs may allow one consolidated **pre-merge** smoke. Do not alter main, lose isolation, use real data or treat this as merge/R4 authorization. Record tested HEADs and revalidate material subsequent changes.

Choose merge order from dependencies, overlap, contracts and risk. When the published/tested/reviewed HEADs agree, required CI is green, no BLOCKER/MAJOR remains and required pre-merge validation is complete, say **LISTO. HAZ MERGE MANUAL #<PR>**. Stop; never run merge/auto-merge or set a human-authorized state on Fernando's behalf.

When Fernando reports **MERGEADO #<PR>**, continue automatically:

1. Verify the PR is actually merged, obtain the new main SHA and relevant CI.
2. Identify remaining approved group PRs and reconcile them in their own worktrees using the repo-appropriate Git strategy; preserve ticket scope and author work.
3. Compare effective diffs against new main and check shared paths/contracts, semantic interaction and conflicts.
4. Resolve only trivial conflicts with unambiguous intent inside scope. On material conflict, report **INTEGRATION DECISION REQUIRED** and stop that PR; independent fronts may continue.
5. Run relevant validation, push reconciled branches, obtain independent review/evidence for exact new HEADs and wait for CI. See reconciliation rules in CODE_REVIEW.
6. Recommend the next ready manual merge and stop at that gate. No additional large technical prompt from Fernando is needed.

## 5. Final verification, acceptance and cleanup

After the last human merge, verify all intended PRs merged, final main SHA/CI and Containers/recovery when applicable, schema/migrations when relevant and absence of material integration drift. Deliver the declared consolidated human smoke if required, with concrete steps and expected results; do not call functional acceptance complete while it is pending.

After Fernando accepts the cycle (for example, **VALIDADO OK**), record acceptance in the existing conversation/PR/issue evidence. A cycle with no applicable human smoke records that justification and human merge decisions instead of inventing test results.

Cleanup only clean, merged, integrated obsolete local branches/worktrees and owned test resources that no remaining front needs. Never delete preexisting/uncommitted work or resources owned by others. Do not remove a dependency worktree needed for reconciliation. Return to DISCOVER from verified main, propose the next group and wait for approval; no automatic new code.

## Short human interface

These are examples of intent, not mandatory literal commands:

| Human intent | Coordinator continuation |
|---|---|
| Continua desde main | DISCOVER and propose; no development without approval |
| APROBADO | Execute only approved group through the next human gate |
| MERGEADO #<PR> | Verify and automatically reconcile remaining PRs |
| VALIDADO OK | Close accepted cycle, safe cleanup, DISCOVER and propose |

None authorizes production deployment, persistent DB mutation, real SRI, real certificates/secrets, secret rotation or irreversible repair. Those remain separate human R4 operations.
