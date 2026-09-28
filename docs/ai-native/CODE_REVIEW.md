# HFPOS — Independent AI Code Review

## Purpose

HFPOS uses independent review as a separate control from implementation and CI. The preferred AI reviewer is **Codex Code Review on GitHub** when available for the repository.

Codex is an additional reviewer. It does not replace automated tests, branch protection, the AI-Native governance check, required human validation for R3 work, or Fernando's final merge authorization.

## Why Codex

The repository already uses `AGENTS.md` as the normative agent policy. Codex Code Review can consume repository review rules and review pull requests directly in GitHub, so it fits the existing workflow without adding an OpenAI API key or custom reviewer service to the repository.

## One-time external enablement

Repository code cannot enable a GitHub/Codex account integration by itself. The GitHub repository must be connected to Codex Code Review once outside the repo.

Expected setup:

1. Connect the GitHub repository `fhamann79/hfpos` to Codex/ChatGPT and create a Codex environment for the repository.
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

## Authoritative evidence

The PR body may contain a human-readable summary of reviewer, state, reviewed SHA and finding counts, but **that text is not authoritative and cannot satisfy the gate**.

For R2/R3, `AI-Native Governance` obtains evidence directly from GitHub while executing trusted default/base-branch code. Codex currently emits either:

- a normal GitHub review with inline findings; or
- a trusted bot issue comment whose canonical prologue says `Codex Review: Didn't find any major issues. Chef's kiss.` and includes a `Reviewed commit` SHA when the review is clean.

The gate accepts only attestations that:

- were emitted by an explicitly trusted independent reviewer identity;
- resolve to the exact current 40-character PR HEAD SHA;
- for normal reviews, carry a full GitHub `commit_id` equal to the current HEAD;
- for clean comments, match the canonical Codex clean-verdict structure and have their displayed SHA resolved through GitHub's commit API to the exact current HEAD;
- were collected by the trusted workflow from GitHub APIs, not supplied by PR code or editable PR prose.

## Persistent finding ledger

Inline comments are useful evidence but they are not an immutable ledger. A repository writer may be able to delete or edit a comment after review. Therefore trusted default-branch workflow code persists a separate commit status named **`AI-Native Review Finding`** on the exact PR HEAD as soon as a trusted `BLOCKER`, `MAJOR`, or `CHANGES_REQUESTED` event is observed.

That ledger is deliberately one-way for an immutable HEAD:

- it only records failure;
- it is never cleared by a clean comment on the same SHA;
- deleting/dismissing/editing the original review evidence cannot make the HEAD ready;
- correcting the code creates a new HEAD with a fresh ledger and requires a fresh review.

The validator combines current GitHub review evidence with this persistent finding ledger. This preserves the principle that `BLOCKER` and `MAJOR` findings are sticky for the commit on which they were found.

## Head-bound governance status

GitHub does not necessarily attach workflows triggered by `issue_comment` to the PR HEAD. To avoid a clean Codex comment leaving a required check permanently stale, trusted workflow code explicitly publishes a commit status named **`AI-Native Governance`** to the exact PR HEAD.

Every relevant governance event first publishes `AI-Native Governance = pending`. After validation it publishes `success` or `failure` on that same SHA. If processing crashes before completion, the latest state remains pending and the PR fails closed once this context is required by the branch ruleset.

The only write permission granted to this workflow is `statuses: write`. It does not receive contents write, pull-request write, secrets, production credentials, SRI credentials or certificate access.

## Automatic re-evaluation

Governance listens for:

- PR changes through `pull_request_target`;
- submitted/edited/dismissed GitHub reviews through `pull_request_review`;
- trusted inline review-comment creation/edit/deletion through `pull_request_review_comment`;
- trusted Codex issue comments through `issue_comment`.

For every event, governance resolves the PR again from GitHub and synthesizes one canonical `pull_request` payload for the validator. It never treats an `issue_comment` payload as if it already contained full PR context.

Therefore a new push makes old review evidence stale, and a subsequent Codex review/clean verdict automatically causes governance to evaluate and publish status for the current HEAD.

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
- can untrusted PR code execute under privileged review events?;
- can a stale review be accepted after a new push?;
- can an agent claim independent review without a real reviewer?;
- can editable PR metadata impersonate trusted review evidence?;
- can one trusted attestation incorrectly erase another finding on the same HEAD?;
- can deletion/editing/dismissal erase a previously observed blocking finding?;
- can a quoted or malformed clean-verdict marker be mistaken for a real clean review?;
- is the final governance status published on the exact PR HEAD?;
- are write permissions limited to the minimum required status publication?

## Severity

- `BLOCKER`: merge forbidden; security/data/tenant/money/stock/migration/gate failure.
- `MAJOR`: important defect or regression risk; must be fixed before merge.
- `MINOR`: useful non-blocking improvement.
- `NIT`: optional detail/style.

Findings should state the concrete failure mode and affected area. Avoid stylistic findings without an actual risk.

## Failure / fallback

If Codex Code Review is unavailable:

1. record that the preferred provider is unavailable;
2. do not pretend that implementer self-review is independent;
3. use a genuinely independent reviewer whose GitHub identity is explicitly trusted by governance;
4. keep the PR blocked for R2/R3 until trusted review evidence exists for the current HEAD.

## CI vs governance responsibilities

Product CI (`Backend` and `Frontend`) should run for code changes, not every edit to PR prose.

`AI-Native Governance` owns PR metadata/risk/review-readiness checks and reacts to PR/review evidence changes. Product CI does not need to rebuild the entire POS for metadata-only edits.

The governance workflow executes trusted default/base-branch code, never executes untrusted PR code, and queries GitHub APIs to verify reviewer identity, canonical reviewed commit and blocker severity. Its sole write capability is publishing commit statuses on the PR HEAD.

After DEV-002 is merged and the explicit `AI-Native Governance` commit status has been demonstrated on a subsequent PR, add **AI-Native Governance** to the `Protect main + CI` ruleset as a required status check.

## Merge authority

Neither Codex nor any other AI reviewer may merge the PR or change the PR to a human-authorized merge state. Fernando makes the final merge decision after required evidence is complete.
