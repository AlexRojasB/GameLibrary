"""Versioned local runner inventory; prior review artifacts are never overwritten."""

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUTPUT = "docs/deployment/neon-qa-source-manifest-runner-preflight-fix.json"
PRIOR = "docs/deployment/neon-qa-source-manifest-runner.json"
EXCLUDED = {"opencode.json", "render.yaml", OUTPUT}


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


old = load(ROOT / "deploy/qa/check-neon-plan.py", "historical_neon_plan")
runner = load(HERE / "run.py", "neon_runner_entry")


def selected():
    prior = json.loads((ROOT / PRIOR).read_text())
    return ({e["path"] for e in prior["entries"]} | {PRIOR}
            | {p.relative_to(ROOT).as_posix() for p in HERE.rglob("*")
               if p.is_file() and not {"bin", "obj", "__pycache__"}.intersection(p.relative_to(HERE).parts)})


def manifest():
    return {
        "status": "local-runner-review-only-not-release-or-execution-approved",
        "revision": "neon-runner-preflight-cast-fix-2026-11-15",
        "previousInventoryPath": PRIOR,
        "previousInventorySha256": hashlib.sha256((ROOT / PRIOR).read_bytes()).hexdigest(),
        "frozenManifestSha256": runner_frozen_hash(),
        "entries": [old.file_record(name) for name in sorted(selected())],
        "excluded": sorted(EXCLUDED),
        "executionApproved": False,
        "gitPublicationApproved": False,
        "livePostgresRunnerGatesPassed": True,
        "localVerification": {
            "postgresVersion": "18.7",
            "actualQueryRegressionChecksPassed": 2,
            "realIntegrationScenariosPassed": 12,
            "remoteConnectionAttempted": False,
            "executionAuthorizationGranted": False,
        },
    }


def runner_frozen_hash():
    return hashlib.sha256((ROOT / old.FROZEN).read_bytes()).hexdigest()


class RunnerInventoryTests(unittest.TestCase):
    def test_current_inventory_and_all_hashes_modes(self):
        self.assertEqual(json.loads((ROOT / OUTPUT).read_text()), manifest())

    def test_no_unknown_git_eligible_input(self):
        actual = {p.decode() for p in subprocess.check_output(
            ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=ROOT).split(b"\0") if p}
        self.assertEqual(actual, selected() | EXCLUDED)
        self.assertFalse(selected() & EXCLUDED)

    def test_historical_manifest_still_matches_its_historical_selection(self):
        self.assertEqual(old.read_json(old.MANIFEST), old.build_manifest())

    def test_original_runner_inventory_preserved_as_historical_evidence(self):
        self.assertEqual(hashlib.sha256((ROOT / PRIOR).read_bytes()).hexdigest(),
                         "9ba4d27d2dcd133f231f71e73e5169a6a1e7c94cff7a98382e6d62e0e67f7a89")

    def test_templates_fail_closed_and_have_exact_target(self):
        auth = json.loads((HERE / "authorization.example.json").read_text())
        metadata = json.loads((HERE / "metadata.example.json").read_text())
        self.assertFalse(auth["approved"])
        self.assertFalse(metadata["verified"])
        self.assertIsNone(auth["runnerInventorySha256"])
        self.assertIsNone(auth["expectedPreflightSha256"])
        self.assertEqual(auth["target"], metadata["target"])
        self.assertEqual(auth["target"]["projectId"], "super-fire-30188358")
        self.assertEqual(auth["target"]["postgresVersionNum"], 180007)
        for key in ["actualConfigurationAccepted", "sourceReleaseApproved", "localPostgresGatesPassed",
                    "exposurePolicyVerified", "transportBoundaryAccepted", "exclusiveMaintenanceConfirmed"]:
            self.assertFalse(auth[key])

    def test_owner_private_input_policy_with_synthetic_empty_secret(self):
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory).resolve()
            parent.chmod(0o700)
            path = parent / "synthetic.json"
            path.write_text("{}")
            path.chmod(0o600)
            self.assertEqual(runner.private_path(str(path)), str(path))
            path.chmod(0o644)
            with self.assertRaises(ValueError):
                runner.private_path(str(path))
            path.chmod(0o600)
            link = parent / "link"
            link.symlink_to(path)
            with self.assertRaises(ValueError):
                runner.private_path(str(link))
            parent.chmod(0o755)
            with self.assertRaises(ValueError):
                runner.private_path(str(path))

    def test_execution_without_private_inputs_rejected_before_build(self):
        result = subprocess.run([sys.executable, str(HERE / "run.py"), "--execute", "--sql", "/nonexistent.sql"],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("separate future authorization", result.stderr)

    def test_offline_refuses_private_inputs_before_touching_them(self):
        result = subprocess.run([sys.executable, str(HERE / "run.py"), "--sql", "/nonexistent.sql",
                                 "--passfile", "/never-read-this-file"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("Offline modes reject", result.stderr)


if __name__ == "__main__":
    if sys.argv[1:] in (["--write"], ["--refresh-local-draft"]):
        data = manifest()
        refresh = sys.argv[1:] == ["--refresh-local-draft"]
        if refresh:
            current = json.loads((ROOT / OUTPUT).read_text())
            if current["status"] != data["status"] or current["executionApproved"] is not False:
                raise SystemExit("Only this unapproved local runner draft may be refreshed")
        with (ROOT / OUTPUT).open("w" if refresh else "x") as output:
            json.dump(data, output, indent=2)
            output.write("\n")
        print(f"Created {OUTPUT}: {len(data['entries'])} inputs; not approval")
    elif sys.argv[1:] == ["--test"]:
        # The old exact-current-path-set assertion predates the runner. Preserve it
        # and its evidence unchanged; its replacement is the strict current set test
        # above. All other historical tests still run, without weakening them.
        suite = unittest.TestSuite()
        for test in unittest.defaultTestLoader.loadTestsFromTestCase(old.NeonPlanTests):
            if test._testMethodName != "test_no_unreviewed_git_eligible_path_is_silently_selected":
                suite.addTest(test)
        suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(RunnerInventoryTests))
        print("Historical path-set assertion superseded by strict runner inventory assertion (historical file preserved).")
        sys.exit(not unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful())
    else:
        raise SystemExit("Use --write (new artifact), --refresh-local-draft (unapproved draft only) or --test (offline)")
