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

    def evidence(
        self,
        *attestations: dict,
        evidence_head: str | None = None,
        sticky_findings: bool = False,
    ) -> dict:
        return {
            "head_sha": evidence_head or self.HEAD,
            "sticky_findings": sticky_findings,
            "attestations": list(attestations),
        }

    def review(
        self,
        *,
        reviewer: str = "chatgpt-codex-connector[bot]",
        commit_id: str | None = None,
        review_id: int = 100,
        blocker: int = 0,
        major: int = 0,
        state: str = "COMMENTED",
        created_at: str = "2026-09-28T15:00:00Z",
    ) -> dict:
        return {
            "id": review_id,
            "kind": "review",
            "reviewer": reviewer,
            "commit_id": commit_id or self.HEAD,
            "state": state,
            "created_at": created_at,
            "blocker_count": blocker,
            "major_count": major,
        }

    def clean_comment(
        self,
        *,
        reviewer: str = "chatgpt-codex-connector[bot]",
        commit_id: str | None = None,
        comment_id: int = 200,
        created_at: str = "2026-09-28T15:05:00Z",
    ) -> dict:
        return {
            "id": comment_id,
            "kind": "clean_comment",
            "reviewer": reviewer,
            "commit_id": commit_id or self.HEAD,
            "state": "CLEAN",
            "created_at": created_at,
            "blocker_count": 0,
            "major_count": 0,
        }

    def test_r1_does_not_require_independent_review(self) -> None:
        self.assertEqual([], review_readiness_errors(None, self.HEAD, "R1"))

    def test_missing_trusted_evidence_blocks_r2(self) -> None:
        errors = review_readiness_errors(None, self.HEAD, "R2")
        self.assertTrue(any("trusted GitHub" in error for error in errors))

    def test_current_codex_review_is_ready(self) -> None:
        evidence = self.evidence(self.review())
        self.assertEqual([], review_readiness_errors(evidence, self.HEAD, "R2"))

    def test_current_codex_clean_comment_is_ready(self) -> None:
        evidence = self.evidence(self.clean_comment())
        self.assertEqual([], review_readiness_errors(evidence, self.HEAD, "R2"))

    def test_untrusted_reviewer_cannot_self_certify(self) -> None:
        evidence = self.evidence(self.review(reviewer="fhamann79"))
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("No active trusted independent reviewer" in error for error in errors))

    def test_stale_attestation_blocks_r2(self) -> None:
        evidence = self.evidence(self.review(commit_id="b" * 40))
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("No active trusted independent reviewer" in error for error in errors))

    def test_short_sha_is_never_authoritative(self) -> None:
        evidence = self.evidence(self.clean_comment(commit_id=self.HEAD[:10]))
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("No active trusted independent reviewer" in error for error in errors))

    def test_stale_evidence_head_blocks_r2(self) -> None:
        evidence = self.evidence(self.review(), evidence_head="b" * 40)
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("stale" in error for error in errors))

    def test_blocker_or_major_blocks_r3(self) -> None:
        evidence = self.evidence(self.review(blocker=1, major=2))
        errors = review_readiness_errors(evidence, self.HEAD, "R3")
        self.assertTrue(any("BLOCKER" in error for error in errors))
        self.assertTrue(any("MAJOR" in error for error in errors))

    def test_later_clean_attestation_cannot_erase_findings_on_same_head(self) -> None:
        evidence = self.evidence(
            self.review(blocker=1, created_at="2026-09-28T15:00:00Z"),
            self.clean_comment(created_at="2026-09-28T15:05:00Z"),
        )
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("BLOCKER" in error for error in errors))

    def test_persistent_ledger_blocks_even_if_original_comment_disappears(self) -> None:
        evidence = self.evidence(self.clean_comment(), sticky_findings=True)
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("BLOCKER" in error for error in errors))

    def test_dismissing_review_cannot_erase_its_findings(self) -> None:
        evidence = self.evidence(
            self.review(blocker=1, state="DISMISSED"),
            self.clean_comment(),
        )
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("BLOCKER" in error for error in errors))

    def test_changes_requested_is_blocking_even_without_marker(self) -> None:
        evidence = self.evidence(
            self.review(state="CHANGES_REQUESTED"),
            self.clean_comment(),
        )
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("MAJOR/change-request" in error for error in errors))

    def test_dismissed_clean_review_alone_is_not_active_evidence(self) -> None:
        evidence = self.evidence(self.review(state="DISMISSED"))
        errors = review_readiness_errors(evidence, self.HEAD, "R2")
        self.assertTrue(any("No active trusted independent reviewer" in error for error in errors))

    def test_findings_from_different_head_do_not_block_current_head(self) -> None:
        evidence = self.evidence(
            self.review(commit_id="b" * 40, blocker=1),
            self.clean_comment(),
        )
        self.assertEqual([], review_readiness_errors(evidence, self.HEAD, "R2"))


class GovernanceWorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        repo_root = Path(__file__).resolve().parents[2]
        cls.workflow = (repo_root / ".github/workflows/governance.yml").read_text(encoding="utf-8")

    def test_clean_verdict_deletion_retriggers_governance(self) -> None:
        self.assertGreaterEqual(self.workflow.count("types: [created, edited, deleted]"), 2)

    def test_finding_ledger_targets_reviewed_commit_or_fails_closed(self) -> None:
        self.assertIn("comment.commit_id", self.workflow)
        self.assertIn("review.commit_id", self.workflow)
        self.assertIn("const targetSha =", self.workflow)
        self.assertIn("sha: targetSha", self.workflow)

    def test_deleted_trusted_review_comment_is_fail_closed(self) -> None:
        self.assertIn("action === 'deleted'", self.workflow)
        self.assertIn("Trusted review evidence was deleted from reviewed commit", self.workflow)

    def test_blocking_event_persistence_is_outside_cancellable_governance(self) -> None:
        persist_start = self.workflow.index("  persist_review_event:")
        governance_start = self.workflow.index("  governance:")
        self.assertLess(persist_start, governance_start)
        persist_block = self.workflow[persist_start:governance_start]
        self.assertNotIn("concurrency:", persist_block)
        self.assertNotIn("cancel-in-progress:", persist_block)

    def test_governance_waits_for_ledger_then_serializes_by_pr(self) -> None:
        governance_start = self.workflow.index("  governance:")
        governance_block = self.workflow[governance_start:]
        self.assertIn("needs: persist_review_event", governance_block)
        self.assertIn("concurrency:", governance_block)
        self.assertIn("group: governance-${{ github.repository }}-${{ github.event.pull_request.number || github.event.issue.number }}", governance_block)
        self.assertIn("cancel-in-progress: true", governance_block)


if __name__ == "__main__":
    unittest.main()
