# HFPOS — Independent AI Code Review

## Purpose

HFPOS uses independent review as a separate control from implementation and CI. The preferred AI reviewer is **Codex Code Review on GitHub** when available for the repository.

Codex is an additional reviewer. It does not replace automated tests, branch protection, the AI-Native governance check, required human validation for R3 work, or Fernando's final merge authorization.

## Why Codex

The repository already uses `AGENTS.md` as the normative agent policy. Codex Code Review can consume repository review rules and review pull requests directly in GitHub, so it fits the existing workflow without adding an OpenAI API key or a custom AI reviewer service to the repository.

## One-time external enablement

Repository code cannot enable GitHub/Codex account integrations or GitHub App credentials by itself. The repository owner performs these one-time external steps:

1. Connect `fhamann79/hfpos` to Codex/ChatGPT and keep the Codex environment for the repository.
2. Enable Codex Code Review for the repository.
3. Create a dedicated GitHub App named **HFPOS Governance Gate**, install it only on `fhamann79/hfpos`, and grant it only the repository permission needed to write commit statuses (plus implicit metadata read).
4. Create the repository environment `hfpos-governance` **before adding secrets**. Configure **Deployment branches and tags → Selected branches and tags → Branch `main` only**. Do not choose `No restriction` or `Protected branches only`.
5. Only after step 4 is saved, add the environment secrets `HFPOS_GOVERNANCE_APP_ID` and `HFPOS_GOVERNANCE_PRIVATE_KEY`.
6. After the App has emitted `AI-Native Governance` on a pilot PR, configure `Protect main + CI` to require that status from the expected **HFPOS Governance Gate** App source, not only by status name.

The App private key is infrastructure credential material. It must never be committed, pasted into PRs, stored as a normal repository-wide secret, or exposed to PR-head workflows. The `main`-only environment policy is a required security boundary: if that restriction is missing or broadened, remove/disable the App secrets until the restriction is restored.

Until automatic Codex review is confirmed, request review in the PR with:

```text
@codex review
```

DEV-002 is the pilot for proving the review contract and the protected governance identity.

## Review lifecycle

For R2/R3 work:

```text
IMPLEMENTATION
  -> PR_OPEN
  -> PRODUCT_CI
  -> CODEX_INDEPENDENT_REVIEW
  -> FIX_FINDINGS
  -> RE-REVIEW_NEW_HEAD
  -> APP-AUTHENTICATED_GOVERNANCE_GREEN
  -> HUMAN_VALIDATION (R3 / when required)
  -> FERNANDO_AUTHORIZES_MERGE
```

If a new commit is pushed after review, the review is stale. The new PR HEAD must be reviewed again before readiness.

## Authoritative review evidence

The PR body may contain a human-readable summary of reviewer, state, reviewed SHA and findings, but **that text is not authoritative and cannot satisfy the gate**.

`AI-Native Governance` reads evidence directly from GitHub while executing trusted default/base-branch code. Observed Codex behavior is deliberately interpreted fail-closed:

- when Codex finds actionable issues, it creates a trusted GitHub review object on the reviewed commit;
- when Codex finds no major issues, it emits a trusted bot issue comment whose canonical first line begins `Codex Review: Didn't find any major issues.` and whose `Reviewed commit` value identifies the reviewed commit.

For R2/R3 readiness:

- the evidence must come from `chatgpt-codex-connector[bot]`;
- a clean comment's displayed short SHA is resolved through GitHub's commits API and must equal the exact current 40-character HEAD;
- **any Codex GitHub review object on the current HEAD blocks that HEAD**, regardless of later edit/delete/dismiss of mutable inline text;
- a positive result requires at least one canonical clean Codex comment for the exact current HEAD and no Codex review object for that HEAD;
- findings are cleared by fixing the code and producing a new HEAD, never by repeatedly re-reviewing the unchanged commit.

This makes the commit itself the review boundary and removes dependence on mutable finding counters or editable PR prose.

## Protected governance identity

A required status name by itself is insufficient: a workflow running from an untrusted same-repository PR may be able to request a writable `GITHUB_TOKEN` and try to publish another status with the same context string.

Therefore HFPOS separates **review evidence**, **trusted workflow code**, and **status publisher identity**:

- `.github/workflows/governance.yml` runs only through trusted default/base-ref event families (`pull_request_target` and `issue_comment`) and never executes PR-head code;
- its built-in `GITHUB_TOKEN` is read-only for repository contents/PR/issues and has **no** `statuses: write` or `checks: write`;
- the final `AI-Native Governance` commit status is written only with a short-lived installation token minted for the dedicated **HFPOS Governance Gate** GitHub App;
- the App ID/private key live only in the protected `hfpos-governance` environment;
- that environment is configured with **Selected branches and tags → Branch `main` only** before either secret is stored;
- the workflow is expected to run with `GITHUB_REF=refs/heads/main` for the trusted event families it uses; the environment branch rule independently enforces the secret boundary;
- the branch ruleset pins the required `AI-Native Governance` context to the expected HFPOS Governance Gate App source.

A PR workflow may imitate the text `AI-Native Governance`, but it cannot possess the required App identity if the environment remains restricted to `main`, so that imitation cannot satisfy the protected rule once the expected source is configured.

## Review request invalidation

A new explicit `@codex review` request on an unchanged HEAD invalidates any previous green governance state while the new review is pending. Trusted governance code uses the dedicated App token to publish `AI-Native Governance = pending` for that exact HEAD.

If Codex returns findings, the current HEAD remains blocked because the GitHub review object is evidence against it. If Codex returns the canonical clean comment, the issue-comment event causes governance to re-evaluate the exact current HEAD.

## Automatic re-evaluation

Governance listens to:

- PR state/code changes through `pull_request_target`;
- PR issue comments through `issue_comment` (`created`, `edited`, `deleted`).

Those event families execute from the trusted default/base ref, which is required for access to the protected governance environment. The workflow never checks out or executes PR code; it fetches the PR HEAD only as a Git object to compute changed paths.

A new push invalidates old review evidence automatically because the exact HEAD changes. Deleting or editing the canonical clean Codex comment triggers another current-state evaluation.

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

- can untrusted PR code execute under a privileged event?;
- can a PR forge the required status/check identity?;
- can a PR-head workflow request the governance environment or read its App secrets?;
- can a stale review be accepted after a new push?;
- can editable PR metadata impersonate trusted review evidence?;
- can deletion/editing/dismissal make a reviewed failing HEAD clean without a code change?;
- can a quoted or malformed clean-verdict marker be mistaken for a real clean review?;
- can a short SHA resolve to anything other than the exact current HEAD?;
- are App credentials reachable only from `main`-ref trusted workflow context?;
- is the ruleset pinned to the expected governance App source rather than only a context string?

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
3. use a genuinely independent reviewer whose identity is explicitly supported by governance;
4. keep R2/R3 blocked until trusted review evidence exists for the current HEAD.

If the HFPOS Governance Gate App, `main`-only protected environment, or expected-source ruleset binding is unavailable, governance is **not fully enforced** and the repository stays in manual human-merge mode until the infrastructure is restored.

## CI vs governance responsibilities

Product CI (`Backend` and `Frontend`) runs from PR code to build/test the proposed implementation. It is not authoritative for independent-review readiness.

`AI-Native Governance` owns metadata/risk/review-readiness and executes only trusted base/default-branch code. Its built-in token is read-only. Status publication uses the dedicated App identity from the protected environment.

After DEV-002 is merged, prove the final workflow on a small pilot PR, then bind **AI-Native Governance** in `Protect main + CI` to the expected **HFPOS Governance Gate** App source before treating the automated gate as mandatory enforcement.

## Merge authority

Neither Codex nor the governance App nor any other AI reviewer may merge a PR or change its human authorization state. Fernando makes the final merge decision after required evidence is complete.
