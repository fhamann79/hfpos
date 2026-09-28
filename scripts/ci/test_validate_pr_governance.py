#!/usr/bin/env python3
"""Regression tests for HFPOS PR governance risk/review classification."""

from pathlib import Path
import unittest

from validate_pr_governance import (
    compute_minimum_risk,
    minimum_risk_for_path,
    review_readiness_errors,
)


class MinimumRiskForPathTests(unittest.TestCase):
    def assert_risk(self, path: str, expected: int) -> None:
        risk, _reason = minimum_risk_for_path(path)
        self.assertEqual(expected, risk, path)

    def test_documentation_is_r0(self) -> None:
        self.assert_risk("docs/ai-native/README.md", 0)

    def test_process_template_is_r1(self) -> None:
        self.assert_risk(".github/pull_request_template.md", 1)

    def test_workflow_and_ci_code_are_r2(self) -> None:
        self.assert_risk(".github/workflows/ci.yml", 2)
        self.assert_risk("scripts/ci/validate_pr_governance.py", 2)

    def test_all_agent_policy_files_are_r2(self) -> None:
        self.assert_risk("AGENTS.md", 2)
        self.assert_risk("backend/Pos.Backend.Api/Pos.Backend.Api/AGENTS.md", 2)
        self.assert_risk("frontend/pos-frontend/AGENTS.md", 2)

    def test_frontend_tests_are_r1_but_runtime_source_is_r2(self) -> None:
        self.assert_risk("frontend/pos-frontend/src/app/features/sales/sales.component.spec.ts", 1)
        self.assert_risk("frontend/pos-frontend/src/app/features/sales/sales.component.ts", 2)
        self.assert_risk("frontend/pos-frontend/src/app/shared/price.component.ts", 2)

    def test_critical_backend_business_paths_are_r3(self) -> None:
        self.assert_risk("backend/Pos.Backend.Api/Pos.Backend.Api/Core/Services/SaleService.cs", 3)
        self.assert_risk("backend/Pos.Backend.Api/Pos.Backend.Api/Migrations/20260928_Test.cs", 3)

    def test_highest_changed_path_wins(self) -> None:
        minimum, evidence = compute_minimum_risk(
            [
                "docs/ai-native/README.md",
                "frontend/pos-frontend/src/app/app.component.ts",
                "backend/Pos.Backend.Api/Pos.Backend.Api/Core/Services/PurchaseService.cs",
            ]
        )
        self.assertEqual("R3", minimum)
        self.assertEqual(3, len(evidence))


class IndependentReviewReadinessTests(unittest.TestCase):
    HEAD = "a" * 40

    def evidence(self, *attestations: dict, evidence_head: str | None = None) -> dict:
        return {
            "head_sha": evidence_head or self.HEAD,
            "attestations": list(attestations),
        }

    def review(
        self,
        *,
        reviewer: str = "chatgpt-codex-connector[bot]",
        commit_id: str | None = None,
        state: str = "COMMENTED",
    ) -> dict:
        return {
            "id": 100,
            "kind": "review",
            "reviewer": reviewer,
            "commit_id": commit_id or self.HEAD,
            "state": state,
            "created_at": "2026-09-28T15:00:00Z",
        }

    def clean_comment(
        self,
        *,
        reviewer: str = "chatgpt-codex-connector[bot]",
        commit_id: str | None = None,
    ) -> dict:
        return {
            "id": 200,
            "kind": "clean_comment",
            "reviewer": reviewer,
            "commit_id": commit_id or self.HEAD,
            "state": "CLEAN",
            "created_at": "2026-09-28T15:05:00Z",
        }

    def test_r1_does_not_require_independent_review(self) -> None:
        self.assertEqual([], review_readiness_errors(None, self.HEAD, "R1"))

    def test_missing_evidence_blocks_r2(self) -> None:
        errors = review_readiness_errors(None, self.HEAD, "R2")
        self.assertTrue(any("trusted GitHub" in error for error in errors))

    def test_clean_codex_comment_on_exact_head_is_ready(self) -> None:
        self.assertEqual(
            [],
            review_readiness_errors(self.evidence(self.clean_comment()), self.HEAD, "R2"),
        )

    def test_any_codex_review_object_on_current_head_blocks_even_if_clean_comment_exists(self) -> None:
        errors = review_readiness_errors(
            self.evidence(self.review(), self.clean_comment()),
            self.HEAD,
            "R2",
        )
        self.assertTrue(any("GitHub review object" in error for error in errors))

    def test_dismissed_review_object_still_blocks_same_immutable_head(self) -> None:
        errors = review_readiness_errors(
            self.evidence(self.review(state="DISMISSED"), self.clean_comment()),
            self.HEAD,
            "R2",
        )
        self.assertTrue(any("GitHub review object" in error for error in errors))

    def test_review_from_old_head_does_not_block_clean_current_head(self) -> None:
        self.assertEqual(
            [],
            review_readiness_errors(
                self.evidence(self.review(commit_id="b" * 40), self.clean_comment()),
                self.HEAD,
                "R2",
            ),
        )

    def test_stale_clean_comment_does_not_satisfy_current_head(self) -> None:
        errors = review_readiness_errors(
            self.evidence(self.clean_comment(commit_id="b" * 40)),
            self.HEAD,
            "R2",
        )
        self.assertTrue(any("No canonical clean" in error for error in errors))

    def test_short_sha_is_never_authoritative_in_validator(self) -> None:
        errors = review_readiness_errors(
            self.evidence(self.clean_comment(commit_id=self.HEAD[:10])),
            self.HEAD,
            "R2",
        )
        self.assertTrue(any("No canonical clean" in error for error in errors))

    def test_stale_evidence_head_blocks(self) -> None:
        errors = review_readiness_errors(
            self.evidence(self.clean_comment(), evidence_head="b" * 40),
            self.HEAD,
            "R2",
        )
        self.assertTrue(any("stale" in error for error in errors))

    def test_untrusted_identity_cannot_satisfy_gate(self) -> None:
        errors = review_readiness_errors(
            self.evidence(self.clean_comment(reviewer="fhamann79")),
            self.HEAD,
            "R2",
        )
        self.assertTrue(any("No canonical clean" in error for error in errors))


class GovernanceWorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        repo_root = Path(__file__).resolve().parents[2]
        cls.workflow = (repo_root / ".github/workflows/governance.yml").read_text(encoding="utf-8")
        cls.agent_policy = (repo_root / "AGENTS.md").read_text(encoding="utf-8")
        cls.review_policy = (repo_root / "docs/ai-native/CODE_REVIEW.md").read_text(encoding="utf-8")

    def test_untrusted_builtin_token_cannot_write_statuses(self) -> None:
        permissions = self.workflow.split("concurrency:", 1)[0]
        self.assertNotIn("statuses: write", permissions)
        self.assertNotIn("checks: write", permissions)

    def test_dedicated_protected_environment_holds_app_credentials(self) -> None:
        self.assertIn("environment: hfpos-governance", self.workflow)
        self.assertIn("HFPOS_GOVERNANCE_APP_ID", self.workflow)
        self.assertIn("HFPOS_GOVERNANCE_PRIVATE_KEY", self.workflow)

    def test_governance_environment_policy_requires_selected_main_only_before_secrets(self) -> None:
        for policy in (self.agent_policy, self.review_policy):
            self.assertIn("Selected branches and tags", policy)
            self.assertIn("Branch `main` only", policy)
            self.assertIn("before", policy.lower())
        self.assertIn("Do not use `No restriction` or `Protected branches only`", self.agent_policy)

    def test_governance_uses_dedicated_github_app_token_for_statuses(self) -> None:
        self.assertIn("actions/create-github-app-token@v2", self.workflow)
        self.assertGreaterEqual(
            self.workflow.count("github-token: ${{ steps.governance-app.outputs.token }}"),
            2,
        )
        self.assertIn("context: 'AI-Native Governance'", self.workflow)

    def test_only_trusted_base_ref_events_are_used(self) -> None:
        self.assertIn("pull_request_target:", self.workflow)
        self.assertIn("issue_comment:", self.workflow)
        self.assertNotIn("pull_request_review:", self.workflow)
        self.assertNotIn("pull_request_review_comment:", self.workflow)

    def test_clean_verdict_deletion_retriggers_evaluation(self) -> None:
        self.assertIn("types: [created, edited, deleted]", self.workflow)

    def test_explicit_review_request_invalidates_previous_green_status(self) -> None:
        self.assertIn("@codex\\s+review", self.workflow)
        self.assertIn("Independent Codex review requested for current HEAD", self.workflow)
        self.assertIn("state: 'pending'", self.workflow)

    def test_current_head_review_objects_are_collected_fail_closed(self) -> None:
        self.assertIn("/pulls/{pr}/reviews", self.workflow)
        self.assertIn("kind\": \"review", self.workflow.replace("'", '"'))

    def test_clean_short_sha_is_resolved_through_github_commits_api(self) -> None:
        self.assertIn("/commits/{encoded}", self.workflow)
        self.assertIn("resolved != head_sha", self.workflow)

    def test_clean_verdict_structure_allows_only_friendly_suffix_variation(self) -> None:
        self.assertIn("clean_header_pattern = re.compile", self.workflow)
        self.assertIn("Didn't find any major issues\\.", self.workflow)
        self.assertIn("lines[3].strip() != \"\"", self.workflow)
        self.assertIn("lines[4].lstrip().startswith(\"<details>\")", self.workflow)
        self.assertNotIn("clean_header = \"Codex Review: Didn't find any major issues. Chef's kiss.\"", self.workflow)


if __name__ == "__main__":
    unittest.main()
