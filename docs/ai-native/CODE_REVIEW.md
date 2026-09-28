# HFPOS — Independent AI Code Review

## Purpose

HFPOS uses independent review as a separate control from implementation and CI. The preferred AI reviewer is **Codex Code Review on GitHub** when available for the repository.

Codex is an additional reviewer. It does not replace automated tests, branch protection, the AI-Native governance check, required human validation for R3 work, or Fernando's final merge authorization.

## Why Codex

The repository already uses `AGENTS.md` as the normative agent policy. Codex Code Review can consume repository review rules and review pull requests directly in GitHub, so it fits the existing workflow without adding an OpenAI API key or custom reviewer service to the repository.

## One-time external enablement

Repository code cannot enable a GitHub/Codex account integration by itself. The GitHub repository must be connected to Codex Code Review once outside the repo.

Expected setup:

1. Connect the GitHub repository `fhamann79/hfpos` to Codex/ChatGPT if it is not already enabled for Codex cloud/code review.
2. Enable Codex Code Review for the repository.
3. Prefer automatic review for new PRs and re-review after new pushes when the product UI/account offers those options.
4. Until automatic review is confirmed, request it manually in the PR with:

```text
@codex review
```

DEV-002 is the pilot PR for proving that this integration is available and produces a real review from a reviewer separate from the implementation pass.

## Review lifecycle

For R2/R3 work:

```text
IMPLEMENTATION
  -> PR_OPEN
  -> PRODUCT_CI
  -> CODEX_INDEPENDENT_REVIEW
  -> FIX_FINDINGS
  -> RE-REVIEW_CURRENT_HEAD
  -> AI-NATIVE_GOVERNANCE_GREEN
  -> HUMAN_VALIDATION (R3 / when required)
  -> FERNANDO_AUTHORIZES_MERGE
```

If a new commit is pushed after review, the review is stale. The current PR HEAD must be reviewed again before readiness.

## Evidence recorded in the PR

The PR body records:

- reviewer provider;
- review state (`PENDIENTE` / `COMPLETA`);
- reviewed HEAD SHA;
- BLOCKER / MAJOR / MINOR / NIT counts;
- short disposition of findings.

R2/R3 is not ready when:

- reviewer is pending;
- review state is not complete;
- reviewed SHA differs from current PR HEAD;
- BLOCKER > 0;
- MAJOR > 0.

## HFPOS review focus

The reviewer must prioritize correctness over style and actively try to falsify assumptions around:

- `CompanyId` tenant isolation;
- establishment / emission-point operational context;
- server-side authorization and permission enforcement;
- sales, purchases, inventory, stock, cash, cancellations and credit notes;
- monetary calculations and state transitions;
- transaction boundaries, locks, stale reads, races and idempotency;
- EF model/migration/constraint consistency;
- SQL correctness, N+1 and unbounded reads;
- SRI document lifecycle, signing boundaries and authorization states;
- frontend permission behavior, API contracts and failure states;
- tests that claim coverage but do not exercise the changed behavior.

Governance changes require a different adversarial lens:

- can the PR weaken or bypass its own checks?;
- can untrusted PR code execute under `pull_request_target`?;
- can a stale review be accepted after a new push?;
- can an agent claim independent review without a real reviewer?;
- can a body edit bypass product CI or required governance?;
- are secrets/write permissions unnecessarily exposed?

## Severity

- `BLOCKER`: merge forbidden; security/data/tenant/money/stock/migration/gate failure.
- `MAJOR`: important defect or regression risk; must be fixed before merge.
- `MINOR`: useful non-blocking improvement.
- `NIT`: optional detail/style.

Findings should state the concrete failure mode and affected area. Avoid stylistic findings without an actual risk.

## Failure / fallback

If Codex Code Review is unavailable:

1. mark the provider/state as unavailable or pending;
2. do not pretend that implementer self-review is independent;
3. use a real human or separate independent reviewer;
4. keep the PR blocked for R2/R3 until independent review is complete.

## CI vs governance responsibilities

Product CI (`Backend` and `Frontend`) should run for code changes, not every edit to PR prose.

`AI-Native Governance` owns PR metadata/risk/review-readiness checks and should react to PR body edits. This separation lets a reviewer update evidence without recompiling the entire POS.

After DEV-002 is merged and the governance workflow has demonstrated its check name on a real PR, add **AI-Native Governance** to the `Protect main + CI` ruleset as a required status check. This GitHub repository setting is intentionally outside autonomous repo code and requires repository-owner administration.

## Merge authority

Neither Codex nor any other AI reviewer may merge the PR or change the PR to a human-authorized merge state. Fernando makes the final merge decision after required evidence is complete.
