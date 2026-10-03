# HFPOS — Independent AI Code Review

## Purpose

HFPOS uses independent review as a separate control from implementation and CI. The preferred AI reviewer is Codex Code Review on GitHub when it is available for the repository.

Codex does not replace automated tests, branch protection, required manual validation for high-risk work, or Fernando's final merge authorization.

## Simple operating flow

For R2 and R3 tickets:

1. Finish implementation on the ticket branch.
2. Run the relevant CI/build/tests.
3. Freeze the PR HEAD: do not keep pushing while the reviewer is working.
4. Request one independent review with a top-level PR comment whose first line is exactly:

   `@codex review`

5. Read all BLOCKER and MAJOR findings together.
6. Fix them in one consolidated pass instead of one micro-commit per finding.
7. Because the HEAD changed, request one fresh Codex review of the new HEAD.
8. When CI is green and the current HEAD has no unresolved BLOCKER/MAJOR, complete merge-blocking human validation. Eligible consolidated cycle smoke requires the explicit coordinator/reviewer justification in root AGENTS.md; retain required validation and risk classification, even when conservative R3 comes from paths.
9. Fernando decides whether to merge.

## Anti-loop rule

Do not repeatedly request Codex review on an unchanged commit. Do not create a new commit only to trigger another review.

A review round exists to find concrete defects. If the reviewer reports findings, batch the corrections, produce one new HEAD and re-review that new HEAD. If review hardening itself starts consuming more time than the product change it protects, stop and move that hardening into a separate ticket instead of expanding the current PR indefinitely.

## Review after reconciliation

The coordinator owns review continuity after each human merge; the implementer is not their own independent reviewer. For a remaining PR reconciled with new main:

- Record old/new HEAD and base; inspect the **effective diff against new main**, conflicts, shared paths and semantic integration, not just the merge commit.
- Rerun relevant checks and require CI on the exact new published HEAD. Previously green CI is not evidence for it.
- A material effective change requires fresh independent review of the new HEAD. Do not carry an obsolete approval across altered contracts, logic or conflict resolutions.
- If the effective patch is unchanged, an independent reconciliation review must still evidence that equivalence and inspect integration with new main. Record reviewer identity, exact HEAD/base, checks and findings in visible review/comment evidence. Prior functional review may be referenced, not silently treated as approval of the new SHA. Request a fresh external round when repository protection or applicable review requirements demand it; do not manufacture no-op commits or repeat full review without new effective risk.
- Coordinator and independent reviewer must justify any consolidated smoke explicitly; security/critical semantics remain pre-merge. Material uncertainty blocks readiness until resolved.

An unrelated defect is reported for later prioritization, not repaired by expanding the PR. Only the affected front pauses for a material integration decision.

## What is authoritative today

DEV-002 deliberately keeps enforcement simple:

- GitHub CI remains the automated build/test gate.
- The repository governance workflow continues validating PR metadata and risk classification.
- Independent-review readiness is checked from the actual Codex review/comment visible in GitHub for the current HEAD.
- The editable PR body is only a human-readable summary; it does not create reviewer identity or approve a merge.
- We are not adding a privileged GitHub App, new secrets, custom attestation ledger, or auto-merge in DEV-002.

A future ticket may automate independent-review enforcement further if the operational value justifies the extra security and maintenance complexity.

## Review focus

For ordinary R2 work, Codex should check scope, contracts, permissions, failure states, regressions, tests and maintainability risks that can cause real defects.

For R3 work, it must additionally challenge tenant isolation, server-side authorization, money/stock/cash semantics, transaction boundaries, locking, concurrency, idempotency, EF migrations/constraints, SQL correctness/performance and SRI lifecycle as applicable.

For governance/CI changes, specifically look for unsafe privileged events, untrusted PR code running with elevated permissions, stale-review assumptions, weakened checks and self-bypass paths.

## Severity

- BLOCKER: merge forbidden until corrected.
- MAJOR: important defect or regression risk; correct before merge.
- MINOR: useful but non-blocking improvement.
- NIT: optional style/detail.

## Merge authority

Codex may review and recommend changes, but it does not authorize merge. CI also does not authorize merge. Fernando makes the final merge decision after required evidence is complete.
