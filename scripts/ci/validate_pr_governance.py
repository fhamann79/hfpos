#!/usr/bin/env python3
"""Validate HFPOS AI-Native pull-request governance metadata.

The script intentionally uses only the Python standard library so it can run in
GitHub Actions without installing dependencies.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path


RISK_ORDER = {"R0": 0, "R1": 1, "R2": 2, "R3": 3, "R4": 4}
TRUSTED_REVIEWERS = {"chatgpt-codex-connector[bot]"}
TICKET_RE = re.compile(r"^(?P<ticket>(?:BE-FE|DEV|BE|FE)-\d+[A-Z]?)\b", re.IGNORECASE)
BODY_TICKET_RE = re.compile(r"-\s*Ticket:\s*`?(?P<ticket>(?:BE-FE|DEV|BE|FE)-\d+[A-Z]?)`?", re.IGNORECASE)
ISSUE_RE = re.compile(r"-\s*Issue:\s*#(?P<issue>\d+)\b", re.IGNORECASE)
BASE_SHA_RE = re.compile(r"-\s*Base SHA al iniciar:\s*`?(?P<sha>[0-9a-f]{40})`?", re.IGNORECASE)
RISK_RE = re.compile(r"-\s*Riesgo declarado:\s*`?(?P<risk>R[0-4])`?", re.IGNORECASE)
REAL_ENV_RE = re.compile(
    r"-\s*Entornos/datos/SRI/certificados reales usados:\s*`?(?P<value>SI|SÍ|NO)`?",
    re.IGNORECASE,
)
MANUAL_REQUIRED_RE = re.compile(r"-\s*Requerida:\s*`?(?P<value>SI|SÍ|NO)`?", re.IGNORECASE)
MERGE_STATE_RE = re.compile(
    r"## Estado de merge\s*\n+\s*`?(?P<state>PENDIENTE|AUTORIZADO_POR_FERNANDO)`?",
    re.IGNORECASE,
)

REQUIRED_SECTIONS = (
    "## Ticket",
    "## Resultado buscado",
    "## Riesgo",
    "## Invariantes protegidos",
    "## Alcance",
    "## No objetivos",
    "## Arquitectura / contratos / datos",
    "## Implementación",
    "## Pruebas ejecutadas",
    "## Validación manual",
    "## Revisión independiente",
    "## Riesgos residuales / rollback",
    "## Estado de merge",
)


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def read_event() -> dict:
    # Governance may synthesize a canonical pull_request payload for events such
    # as issue_comment. Product CI falls back to GitHub's original event file.
    event_path = os.environ.get("HFPOS_EVENT_PATH") or os.environ.get("GITHUB_EVENT_PATH")
    if not event_path:
        raise RuntimeError("Neither HFPOS_EVENT_PATH nor GITHUB_EVENT_PATH is defined")
    payload = json.loads(Path(event_path).read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise RuntimeError("Governance event payload must be a JSON object")
    return payload


def read_changed_files() -> list[str]:
    path = os.environ.get("HFPOS_CHANGED_FILES")
    if not path:
        return []
    file_path = Path(path)
    if not file_path.exists():
        return []
    return [line.strip() for line in file_path.read_text(encoding="utf-8").splitlines() if line.strip()]


def read_review_evidence() -> dict | None:
    path = os.environ.get("HFPOS_REVIEW_EVIDENCE")
    if not path:
        return None
    file_path = Path(path)
    if not file_path.exists():
        return None
    payload = json.loads(file_path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise RuntimeError("HFPOS review evidence must be a JSON object")
    return payload


def env_truthy(name: str) -> bool:
    return os.environ.get(name, "").strip().lower() in {"1", "true", "yes", "si", "sí"}


def minimum_risk_for_path(path: str) -> tuple[int, str]:
    p = path.replace("\\", "/")
    low = p.lower()

    if low == "agents.md" or low.endswith("/agents.md"):
        return 2, "agent governance policy"

    if low.startswith(".github/workflows/") or low.startswith("scripts/ci/"):
        return 2, "repository governance/CI behavior"

    if low.startswith(".github/issue_template/") or low == ".github/pull_request_template.md":
        return 1, "development-process metadata"

    if low.startswith("backend/"):
        if "/tests/" in low or low.endswith("tests.cs") or ".tests/" in low:
            return 1, "backend tests"

        critical_markers = (
            "/migrations/",
            "/security/",
            "/auth",
            "jwt",
            "operationalcontext",
            "/infrastructure/data/",
            "/repositories/",
            "/core/services/",
            "sale",
            "purchase",
            "inventory",
            "stock",
            "cash",
            "creditnote",
            "sri",
            "invoice",
        )
        if any(marker in low for marker in critical_markers):
            return 3, "critical backend transactional/security/data path"
        return 2, "backend application behavior"

    if low.startswith("frontend/pos-frontend/"):
        if ".spec.ts" in low or "/tests/" in low:
            return 1, "frontend tests"
        if low.startswith("frontend/pos-frontend/src/"):
            return 2, "frontend application behavior"
        if low.endswith(("package.json", "package-lock.json", "angular.json", "tsconfig.json")):
            return 2, "frontend dependency/build configuration"
        return 1, "frontend tooling/configuration"

    if low.endswith((".csproj", ".sln", "nuget.config", "directory.packages.props")):
        return 2, "build/dependency configuration"

    if "production" in low or "deploy" in low or low.endswith((".pfx", ".p12", ".key", ".pem")):
        return 3, "production/deployment/certificate-sensitive path"

    if low.startswith("docs/") or low.endswith(".md"):
        return 0, "documentation"

    return 1, "unclassified repository change"


def compute_minimum_risk(paths: list[str]) -> tuple[str, list[tuple[str, str, str]]]:
    highest = 0
    evidence: list[tuple[str, str, str]] = []
    for path in paths:
        rank, reason = minimum_risk_for_path(path)
        risk = f"R{rank}"
        evidence.append((path, risk, reason))
        highest = max(highest, rank)
    return f"R{highest}", evidence


def review_readiness_errors(
    review_evidence: dict | None,
    current_head_sha: str,
    declared_risk: str | None,
) -> list[str]:
    """Validate trusted GitHub review attestations for R2/R3 PRs.

    PR-body review fields are deliberately not authoritative. Only attestations
    collected by trusted default/base-branch workflow code from GitHub APIs can
    satisfy this gate.
    """
    if declared_risk not in {"R2", "R3"}:
        return []

    errors: list[str] = []
    if not review_evidence:
        return ["R2/R3 readiness requires trusted GitHub independent-review evidence."]

    current_head = current_head_sha.lower()
    if not re.fullmatch(r"[0-9a-f]{40}", current_head):
        return ["Current PR HEAD is missing or is not a full 40-character SHA."]

    evidence_head = str(review_evidence.get("head_sha") or "").lower()
    if evidence_head != current_head:
        fail(errors, "Trusted review evidence is stale or does not match the current PR HEAD.")

    attestations = review_evidence.get("attestations")
    if not isinstance(attestations, list):
        return errors + ["Trusted review evidence does not contain an attestation list."]

    eligible: list[dict] = []
    for attestation in attestations:
        if not isinstance(attestation, dict):
            continue
        reviewer = str(attestation.get("reviewer") or "")
        kind = str(attestation.get("kind") or "")
        state = str(attestation.get("state") or "").upper()
        commit_id = str(attestation.get("commit_id") or "").lower()
        if reviewer not in TRUSTED_REVIEWERS:
            continue
        # The collector must resolve every attestation to a canonical full SHA.
        if commit_id != current_head or not re.fullmatch(r"[0-9a-f]{40}", commit_id):
            continue
        if kind == "review" and state not in {"COMMENTED", "APPROVED"}:
            continue
        if kind == "clean_comment" and state != "CLEAN":
            continue
        if kind not in {"review", "clean_comment"}:
            continue
        eligible.append(attestation)

    if not eligible:
        return errors + [
            "No trusted independent reviewer attestation exists for the current PR HEAD."
        ]

    # Findings are sticky for an immutable HEAD. A later clean verdict MUST NOT
    # erase an earlier BLOCKER/MAJOR on the same commit. Fixing a code finding
    # necessarily creates a new HEAD, which then requires a fresh review.
    blocker_count = sum(int(item.get("blocker_count") or 0) for item in eligible)
    major_count = sum(int(item.get("major_count") or 0) for item in eligible)

    if blocker_count != 0:
        fail(errors, f"Trusted independent review has {blocker_count} unresolved BLOCKER finding(s) on this HEAD.")
    if major_count != 0:
        fail(errors, f"Trusted independent review has {major_count} unresolved MAJOR finding(s) on this HEAD.")

    return errors


def main() -> int:
    errors: list[str] = []
    event = read_event()
    pr = event.get("pull_request")
    if not isinstance(pr, dict):
        fail(errors, "Governance event must contain a canonical pull_request object; fail closed.")
        pr = {}

    title = (pr.get("title") or "").strip()
    body = pr.get("body") or ""
    current_head_sha = ((pr.get("head") or {}).get("sha") or "").strip()
    changed_files = read_changed_files()

    if not changed_files:
        fail(errors, "No changed files were supplied to governance validation; fail closed.")

    title_match = TICKET_RE.match(title)
    if not title_match:
        fail(errors, "PR title must start with DEV-/BE-/FE-/BE-FE- ticket code.")
        title_ticket = None
    else:
        title_ticket = title_match.group("ticket").upper()

    for section in REQUIRED_SECTIONS:
        if section not in body:
            fail(errors, f"Missing required PR section: {section}")

    body_ticket_match = BODY_TICKET_RE.search(body)
    body_ticket = body_ticket_match.group("ticket").upper() if body_ticket_match else None
    if not body_ticket:
        fail(errors, "PR body must declare '- Ticket: <ticket-code>'.")
    elif body_ticket == "DEV-000":
        fail(errors, "PR template placeholder DEV-000 must be replaced.")
    elif title_ticket and body_ticket != title_ticket:
        fail(errors, f"Title ticket {title_ticket} does not match body ticket {body_ticket}.")

    if not ISSUE_RE.search(body):
        fail(errors, "PR body must link a GitHub issue using '- Issue: #<number>'.")

    base_match = BASE_SHA_RE.search(body)
    if not base_match:
        fail(errors, "PR body must contain a 40-character 'Base SHA al iniciar'.")
    elif set(base_match.group("sha")) == {"0"}:
        fail(errors, "Base SHA still contains the template placeholder.")

    risk_match = RISK_RE.search(body)
    declared_risk = risk_match.group("risk").upper() if risk_match else None
    if not declared_risk:
        fail(errors, "PR body must declare Riesgo declarado as R0-R4.")

    real_env_match = REAL_ENV_RE.search(body)
    if not real_env_match:
        fail(errors, "PR body must declare whether real environments/data/SRI/certificates were used.")
    elif real_env_match.group("value").upper().replace("Í", "I") != "NO":
        fail(errors, "Autonomous PR validation forbids use of real environments/data/SRI/certificates.")

    merge_match = MERGE_STATE_RE.search(body)
    if not merge_match:
        fail(errors, "PR body must have Estado de merge = PENDIENTE or AUTORIZADO_POR_FERNANDO.")

    minimum_risk, evidence = compute_minimum_risk(changed_files)
    if declared_risk and RISK_ORDER[declared_risk] < RISK_ORDER[minimum_risk]:
        fail(errors, f"Declared risk {declared_risk} is lower than path-derived minimum {minimum_risk}.")

    if declared_risk == "R4":
        fail(errors, "R4 work cannot be executed through the autonomous PR flow; use a human-approved runbook.")

    if declared_risk == "R3":
        manual_match = MANUAL_REQUIRED_RE.search(body)
        if not manual_match or manual_match.group("value").upper().replace("Í", "I") != "SI":
            fail(errors, "R3 PRs must declare manual validation as Requerida: SI.")

    migration_changed = any("/migrations/" in path.replace("\\", "/").lower() for path in changed_files)
    if migration_changed and re.search(r"-\s*Migraciones EF:\s*Ninguna\b", body, re.IGNORECASE):
        fail(errors, "Migration files changed but PR body says 'Migraciones EF: Ninguna'.")

    require_review_ready = env_truthy("HFPOS_REQUIRE_REVIEW_READY")
    if require_review_ready:
        errors.extend(
            review_readiness_errors(read_review_evidence(), current_head_sha, declared_risk)
        )

    print(f"Ticket: {title_ticket or body_ticket or 'UNKNOWN'}")
    print(f"Declared risk: {declared_risk or 'MISSING'}")
    print(f"Path-derived minimum risk: {minimum_risk}")
    print(f"Review readiness enforced: {'YES' if require_review_ready else 'NO'}")
    print(f"Changed files: {len(changed_files)}")
    for path, risk, reason in evidence:
        print(f"  {risk}  {path}  ({reason})")

    if errors:
        print("\nGovernance validation FAILED:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("\nGovernance validation PASSED.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # fail closed for CI/governance code
        print(f"Governance validator crashed: {exc}", file=sys.stderr)
        raise
