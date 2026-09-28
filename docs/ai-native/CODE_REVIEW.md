# HFPOS — Independent AI Code Review

## Purpose

HFPOS uses independent review as a separate control from implementation and CI. The preferred AI reviewer is **Codex Code Review on GitHub**.

The goal is practical: catch defects that tests miss without turning review automation into a second software product.

## Review flow

For R2/R3 work:

```text
IMPLEMENT
  -> OPEN PR
  -> RUN CI
  -> REQUEST CODEX REVIEW
  -> FIX BLOCKER/MAJOR
  -> PUSH NEW HEAD
  -> REQUEST CODEX RE-REVIEW
  -> HUMAN VALIDATION WHEN REQUIRED
  -> FERNANDO AUTHORIZES MERGE
```

Request Codex with a PR comment whose first line is:

```text
@codex review
```

## Current-HEAD rule

Independent review is valid only for the current PR HEAD.

- Record the exact HEAD SHA before requesting review.
- If Codex reviews that SHA and finds BLOCKER/MAJOR issues, fix them in the same branch.
- Any new commit makes the previous review stale.
- After a new commit, request a fresh Codex review.
- Before asking Fernando to merge, verify that CI is green and the latest Codex result corresponds to the current HEAD.

## Severity

- `BLOCKER`: merge forbidden until corrected.
- `MAJOR`: important defect/regression risk; correct before merge.
- `MINOR`: useful improvement that normally does not block merge.
- `NIT`: optional detail/style.

## Review focus for HFPOS

Codex should actively challenge, when applicable:

- `CompanyId` tenant isolation;
- establishment/emission-point operational context;
- server-side authorization;
- money, sales, purchases, inventory, stock, cash, cancellations and credit notes;
- transactions, locks, races and idempotency;
- EF model/migrations/constraints;
- SQL correctness, N+1 and unbounded reads;
- SRI lifecycle and signing boundaries;
- frontend permissions, API contracts and failure states;
- tests that claim coverage without exercising the changed behavior.

For governance/CI changes, additionally check for:

- execution of untrusted PR code in privileged contexts;
- accidental secret exposure;
- weakened branch/CI controls;
- stale-review acceptance;
- misleading test or review evidence.

## What DEV-002 deliberately does not automate

DEV-002 does **not** make GitHub Actions infer or cryptographically enforce Codex review state. We tried that design during the pilot and it created disproportionate complexity around event ordering, mutable comments, status identity and concurrency.

Instead, the orchestrator performs a simple final readiness check before asking Fernando to merge:

1. read current PR HEAD;
2. confirm required CI is green on that HEAD;
3. inspect the latest Codex review/result;
4. confirm it corresponds to that HEAD;
5. confirm no unresolved BLOCKER/MAJOR applies to that HEAD;
6. request required human validation;
7. only then ask Fernando for merge authorization.

This keeps the process auditable without adding privileged GitHub Apps, repository secrets or a custom review-attestation subsystem.

If stronger automated enforcement becomes necessary later, it must be a separate ticket with a narrow threat model, measurable benefit and a bounded implementation.

## Independence and authority

A self-review by the implementation author is not independent review. It may be used as an additional pre-check only.

Codex, CI and other agents never authorize or execute merge. Fernando retains final merge authority.

No review workflow may require production credentials, real SRI certificates, real taxpayer data or persistent/shared database access.
