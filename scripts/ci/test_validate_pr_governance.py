#!/usr/bin/env python3
"""Regression tests for HFPOS PR governance risk/review classification."""

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
        *,
        reviewer: str = "chatgpt-codex-connector[bot]",
        commit_id: str | None = None,
        review_id: int = 100,
        blocker: int = 0,
        major: int = 0,
        state: str = "COMMENTED",
        evidence_head: str | None = None,
    ) -> dict:
        return {
            "head_sha": evidence_head or self.HEAD,
            "reviews": [
                {
                    "id": review_id,
                    "reviewer": reviewer,
                    "commit_id": commit_id or self.HEAD,
                    "state": state,
                    "blocker_count": blocker,
                    "major_count": major,
                }
            ],
        }

    def test_r1_does_not_require_independent_review(self) -> None:
        self.assertEqual([], review_readiness_errors(None, self.HEAD, "R1"))

    def test_missing_trusted_evidence_blocks_r2(self) -> None:
        errors = review_readiness_errors(None, self.HEAD, "R2")
        self.assertTrue(any("trusted GitHub" in error for error in errors))

    def test_complete_current_codex_review_is_ready(self) -> None:
        self.assertEqual([], review_readiness_errors(self.evidence(), self.HEAD, "R2"))

    def test_untrusted_reviewer_cannot_self_certify(self) -> None:
        errors = review_readiness_errors(
            self.evidence(reviewer="fhamann79"), self.HEAD, "R2"
        )
        self.assertTrue(any("No trusted independent reviewer" in error for error in errors))

    def test_stale_review_commit_blocks_r2(self) -> None:
        errors = review_readiness_errors(
            self.evidence(commit_id="b" * 40), self.HEAD, "R2"
        )
        self.assertTrue(any("No trusted independent reviewer" in error for error in errors))

    def test_stale_evidence_head_blocks_r2(self) -> None:
        errors = review_readiness_errors(
            self.evidence(evidence_head="b" * 40), self.HEAD, "R2"
        )
        self.assertTrue(any("stale" in error for error in errors))

    def test_blocker_or_major_blocks_r3(self) -> None:
        errors = review_readiness_errors(
            self.evidence(blocker=1, major=2), self.HEAD, "R3"
        )
        self.assertTrue(any("BLOCKER" in error for error in errors))
        self.assertTrue(any("MAJOR" in error for error in errors))

    def test_latest_current_review_wins(self) -> None:
        evidence = self.evidence(review_id=100, blocker=1)
        evidence["reviews"].append(
            {
                "id": 101,
                "reviewer": "chatgpt-codex-connector[bot]",
                "commit_id": self.HEAD,
                "state": "COMMENTED",
                "blocker_count": 0,
                "major_count": 0,
            }
        )
        self.assertEqual([], review_readiness_errors(evidence, self.HEAD, "R2"))


if __name__ == "__main__":
    unittest.main()
