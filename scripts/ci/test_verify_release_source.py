#!/usr/bin/env python3
"""Execute the real release verifier and workflow gate in disposable Git repos."""

import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / "scripts/ci/verify_release_source.py"
WORKFLOW = ROOT / ".github/workflows/container-images.yml"


class ReleaseSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="hfpos-dev531-release-")
        cls.root = Path(cls.temporary.name)
        cls.repo = cls.root / "checkout"
        cls.repo.mkdir()
        cls.origin = cls.root / "origin.git"
        cls.env = os.environ.copy()
        cls.env.update(GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=os.devnull,
                       GIT_TERMINAL_PROMPT="0")
        cls.git("init", "--bare", str(cls.origin))
        cls.git("init", "--initial-branch=main")
        cls.git("config", "user.name", "Synthetic release fixture")
        cls.git("config", "user.email", "release@example.invalid")
        cls.git("commit", "--allow-empty", "-m", "synthetic ancestor")
        cls.ancestor = cls.git("rev-parse", "HEAD")
        cls.git("commit", "--allow-empty", "-m", "synthetic main")
        cls.main = cls.git("rev-parse", "HEAD")
        cls.git("remote", "add", "origin", str(cls.origin))
        cls.git("push", "origin", "main")
        cls.git("checkout", "-b", "feature")
        cls.git("commit", "--allow-empty", "-m", "unmerged feature")
        cls.feature = cls.git("rev-parse", "HEAD")
        cls.git("checkout", "main")

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    @classmethod
    def git(cls, *arguments):
        result = subprocess.run(["git", *arguments], cwd=cls.repo, env=cls.env,
                                capture_output=True, text=True, check=True)
        return result.stdout.strip()

    def verify(self, release, commit):
        return subprocess.run([sys.executable, str(HELPER), release, commit],
                              cwd=self.repo, env=self.env, capture_output=True, text=True)

    def test_stable_and_rc_on_main_and_ancestor(self):
        for release, commit in (("v0.0.0", self.main), ("v12.31.100", self.ancestor),
                                ("v0.1.0-rc.1", self.main), ("v1.2.3-rc.21", self.ancestor)):
            with self.subTest(release=release):
                self.git("tag", "-f", release, commit)
                result = self.verify(release, commit)
                self.assertEqual(0, result.returncode, result.stderr)

    def test_unmerged_feature_tag_is_rejected(self):
        self.git("tag", "-f", "v7.0.0-rc.1", self.feature)
        self.assertNotEqual(0, self.verify("v7.0.0-rc.1", self.feature).returncode)

    def test_malformed_versions_fail(self):
        for release in ("1.2.3", "v01.2.3", "v1.02.3", "v1.2.03", "v1.2.3-rc.0",
                        "v1.2.3-rc.01", "v1.2.3-rc.1.2", "v1.2.3-alpha.1",
                        "v1.2.3+build", "v1.2.3-rc.1+build", "v1.2.3\n", "--help"):
            with self.subTest(release=release):
                self.assertNotEqual(0, self.verify(release, self.main).returncode)

    def test_tag_and_commit_must_match(self):
        self.git("tag", "-f", "v8.0.0", self.ancestor)
        self.assertNotEqual(0, self.verify("v8.0.0", self.main).returncode)
        self.assertNotEqual(0, self.verify("v99.0.0", self.main).returncode)

    def workflow_gate(self):
        lines = WORKFLOW.read_text(encoding="utf-8").splitlines()
        start = lines.index("      - name: Require an exact release version and a commit from main")
        run = next(index for index in range(start, len(lines)) if lines[index] == "        run: |")
        script = []
        for line in lines[run + 1:]:
            if line and not line.startswith("          "):
                break
            script.append(line[10:])
        return "\n".join(script)

    def test_inline_ancestry_precedes_even_a_malicious_tag_helper(self):
        script = self.workflow_gate()
        self.assertLess(script.index("git merge-base --is-ancestor"), script.index("python"))
        self.git("checkout", "feature")
        path = self.repo / "scripts/ci/verify_release_source.py"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("print('UNMERGED_HELPER_EXECUTED')\nraise SystemExit(0)\n", encoding="utf-8")
        self.git("add", "scripts/ci/verify_release_source.py")
        self.git("commit", "-m", "synthetic malicious feature helper")
        commit = self.git("rev-parse", "HEAD")
        self.git("tag", "-f", "v7.0.0-rc.1", commit)
        bash = (Path(shutil.which("git")).parent.parent / "bin/bash.exe"
                if os.name == "nt" else shutil.which("bash"))
        env = dict(self.env, RELEASE="v7.0.0-rc.1", GITHUB_SHA=commit)
        try:
            result = subprocess.run([str(bash), "-c", script], cwd=self.repo, env=env,
                                    capture_output=True, text=True)
        finally:
            self.git("checkout", "main")
        self.assertNotEqual(0, result.returncode, result.stderr)
        self.assertNotIn("UNMERGED_HELPER_EXECUTED", result.stdout)

    def test_inline_stable_and_rc_execute_the_real_helper(self):
        path = self.repo / "scripts/ci/verify_release_source.py"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(HELPER.read_text(encoding="utf-8"), encoding="utf-8")
        bash = (Path(shutil.which("git")).parent.parent / "bin/bash.exe"
                if os.name == "nt" else shutil.which("bash"))
        for release in ("v2.0.0", "v2.0.0-rc.1"):
            with self.subTest(release=release):
                self.git("tag", "-f", release, self.ancestor)
                env = dict(self.env, RELEASE=release, GITHUB_SHA=self.ancestor)
                result = subprocess.run([str(bash), "-c", self.workflow_gate()],
                                        cwd=self.repo, env=env, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertIn("RELEASE SOURCE PASS", result.stdout)

    def test_failed_fetch_cannot_reuse_stale_main(self):
        self.git("tag", "-f", "v9.0.0", self.main)
        self.git("remote", "set-url", "origin", str(self.root / "missing-origin.git"))
        try:
            self.assertNotEqual(0, self.verify("v9.0.0", self.main).returncode)
        finally:
            self.git("remote", "set-url", "origin", str(self.origin))

    def test_publication_has_only_one_privileged_job_after_readonly_gate(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        verify, publish = workflow.split("  publish:\n", 1)
        self.assertEqual(1, workflow.count("packages: write"))
        self.assertNotIn("packages: write", verify)
        self.assertIn("    needs: verify-release-source", publish)
        self.assertIn("needs.verify-release-source.result == 'success'", publish)
        self.assertIn("github.event_name == 'push' && startsWith(github.ref, 'refs/tags/')", publish)
        self.assertIn("    permissions:\n      contents: read", verify)
        self.assertIn("          persist-credentials: false", verify)
        for contract in ("          provenance: true", "          sbom: true", ":sha-${{ github.sha }}"):
            self.assertIn(contract, publish)


if __name__ == "__main__":
    unittest.main()
