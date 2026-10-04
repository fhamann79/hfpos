# HFPOS — Independent AI Code Review

## Purpose

HFPOS uses independent review as a separate control from implementation and CI. The preferred AI reviewer is Codex Code Review on GitHub when it is available for the repository.

Codex does not replace automated tests, branch protection, approved scope or required human validation. The root policy on `main` defines conditional coordinator merge authority; independent review cannot grant authority or waive a critical barrier.

## Simple operating flow

For R2 and R3 tickets:

1. Finish implementation on the ticket branch.
2. Run the relevant CI/build/tests.
3. Freeze the PR HEAD: do not keep pushing while the reviewer is working.
4. Request one independent review with a top-level PR comment whose first line is exactly:

   `@codex review`

5. Read all BLOCKER and MAJOR findings together.
6. Fix them in one consolidated pass instead of one micro-commit per finding.
7. Review corrections proportionally: for a focused fix, request review of the **delta plus integration impact**, referencing prior functional evidence. A changed SHA alone does not require restarting full review; material changes do. Evidence must cover the final effective change and its exact HEAD/base.
8. When CI is green and the current HEAD has no unresolved BLOCKER/MAJOR, complete merge-blocking human validation. Eligible consolidated cycle smoke requires the explicit coordinator/reviewer justification in root AGENTS.md; retain required validation and risk classification, even when conservative R3 comes from paths.
9. The coordinator applies the root conditional gate. Pending human validation, material doubt, `NO MERGE` or R4 blocks autonomous execution; the policy-introduction PR remains manual.

## Anti-loop rule

Do not repeatedly request Codex review on an unchanged commit. Do not create a new commit only to trigger another review.

A review round finds concrete defects, not ceremonial approval counts. Batch BLOCKER/MAJOR corrections into one reasonable pass; check the correction delta and integration without repeating demonstrated unchanged behavior. Do not review the review absent a concrete new risk or create micro-commit/review loops. If hardening exceeds the product change's value, report it for separately approved work, not scope expansion.

## Review after reconciliation

The coordinator owns review continuity after each human merge; the implementer is not their own independent reviewer. For a remaining PR reconciled with new main:

- Record old/new HEAD and base; inspect the **effective diff against new main**, conflicts, shared paths and semantic integration, not just the merge commit.
- Require CI on the exact new published HEAD. Previously green CI is not CI for it; prior tests/reviews may be referenced only with their real SHA/scope and justified unchanged behavior/integration assumptions.
- A material effective change requires fresh independent review of the new HEAD. Do not carry an obsolete approval across altered contracts, logic or conflict resolutions.
- If there are no conflicts, materially shared paths, changed shared contracts or new functional interaction and the effective patch is identical/equivalent, use **lightweight independent integration/equivalence review**. Record reviewer identity, exact old/new HEAD/base, equivalence method, integration checks and findings visibly. Push and wait for mandatory new-HEAD CI; do not repeat full local suites, PostgreSQL, EF, recovery or full functional review by default. Prior functional review is referenced, never silently relabeled as new-SHA review. Request a fresh external round only when protection/applicable requirements or material risk demand it.
- Any conflict, effective change or material interaction requires proportional additional checks/review. Preserve critical pre-merge human barriers; uncertainty fails closed.
- Coordinator and independent reviewer must justify any consolidated smoke explicitly; security/critical semantics remain pre-merge. Material uncertainty blocks readiness until resolved.

An unrelated defect is reported for later prioritization, not repaired by expanding the PR. Only the affected front pauses for a material integration decision.

## What is authoritative today

Keep enforcement simple:

- GitHub CI remains the automated build/test gate.
- The repository governance workflow continues validating PR metadata and risk classification.
- Independent-review readiness comes from a reviewer who did not author the change, with visible review/comment evidence covering the current HEAD/base, including justified delta/equivalence review.
- The editable PR body is only a human-readable summary; it does not create reviewer identity or approve a merge.
- Conditional merge is an evidenced coordinator decision under root policy, not a new privileged GitHub App, secret, attestation ledger or bypass workflow. Do not arm unattended auto-merge before gates pass.

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

An independent reviewer reports findings; it does not self-authorize its own implementation or invent human acceptance. Approved scope plus the policy **already in main** delegates conditional execution to the coordinator only after every root gate passes. CI alone never authorizes merge. Human pre-merge validation is required for critical effective semantics/material doubt; after `VALIDADO OK`, recheck remaining conditions. R4 remains human-only. A PR changing merge authority cannot apply its proposed rules to itself; the introducing PR ends at the manual-merge gate.
