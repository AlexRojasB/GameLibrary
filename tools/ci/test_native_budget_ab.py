"""Frozen matrix preparation tests only; never execute Linux workloads."""

import importlib.util
import json
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("budget_matrix", Path(__file__).with_name("native-budget-ab.py"))
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)


class MatrixTests(unittest.TestCase):
    def test_exact_eighteen_balanced_conditions(self):
        expected = {f"{budget}-{mode}" for budget in (512, 768, 1024) for mode in ("minimal", "detailed")}
        self.assertEqual(18, sum(len(order) for order in matrix.ORDERS.values()))
        for order in matrix.ORDERS.values():
            self.assertEqual(expected, set(order))
            self.assertEqual(6, len(order))
        self.assertEqual(671088640, matrix.plan()["threshold_bytes"])

    def test_default_plan_never_executes(self):
        with patch.object(matrix.subprocess, "Popen", side_effect=AssertionError("must not execute")):
            self.assertEqual("diagnostic-only", matrix.plan()["kind"])
        result = subprocess.run(["python3", "-B", str(Path(matrix.__file__)), "--plan"], capture_output=True, text=True, check=True)
        self.assertEqual(18, json.loads(result.stdout)["workloads"])

    def test_execution_requires_exact_explicit_opt_in_before_external_access(self):
        with patch.object(matrix.subprocess, "check_output", side_effect=AssertionError("must not access")):
            for approval, sha in (("", "a" * 40), (matrix.APPROVAL, "short")):
                with self.assertRaises(ValueError):
                    matrix.execute(1, Path("unused"), sha, approval)

    def test_six_overlays_preserve_threshold_and_security(self):
        source = {path: (matrix.ROOT / path).read_text() for path in matrix.OVERLAYS}
        for budget in (512, 768, 1024):
            for detailed in (False, True):
                with self.subTest(budget=budget, detailed=detailed):
                    with patch.object(matrix, "blob", return_value="// Diagnostic values only:\ninternal static class NativeMemoryDiagnostics { }\n"):
                        files = matrix.overlay(source, budget, detailed)
                    resource = files[matrix.OVERLAYS[0]]
                    self.assertIn(f"MemoryLimitBytes = {budget}L", resource)
                    self.assertIn("PeakLimitBytes = 640L", resource)
                    runner = files[matrix.OVERLAYS[2]]
                    for flag in ("--cpus 2 --pids-limit 256 --read-only", "--cap-drop ALL",
                                 "--security-opt no-new-privileges", '--network "container:$container"'):
                        self.assertIn(flag, runner)
                    self.assertIn(f"--memory {budget}m --memory-swap {budget}m", runner)
                    self.assertIn(f"MEMORY = {budget * 1048576}", files[matrix.OVERLAYS[3]])
                    self.assertEqual(detailed, "NativeMemoryDiagnostics.Write" in files[matrix.OVERLAYS[1]])
                    self.assertEqual(detailed, "native_sampler=$!" in runner)
                    self.assertIn("await BufferedResponseBody.AssertLengthAsync(response.Content, 2097152, token)", files[matrix.OVERLAYS[1]])
                    self.assertIn("Assert.Equal(18, cases)", files[matrix.OVERLAYS[1]])
        self.assertIn("MemoryLimitBytes = 768L", source[matrix.OVERLAYS[0]])
        self.assertNotIn("NativeMemoryDiagnostics", source[matrix.OVERLAYS[1]])

    def test_changed_or_unapproved_overlay_is_rejected(self):
        with self.assertRaises(ValueError):
            matrix.overlay({}, 2048, False)
        with self.assertRaises(ValueError):
            matrix.once("duplicate duplicate", "duplicate", "replacement")

    def test_observer_privacy(self):
        self.assertIsNone(matrix.safe_detail("NATIVE_MEMORY secret=SENSITIVE-CANARY"))
        self.assertIsNone(matrix.safe_detail('NATIVE_EXTERNAL {"argv":"SENSITIVE-CANARY"}'))
        self.assertIsNone(matrix.safe_detail('NATIVE_EXTERNAL {"reason":"SENSITIVE-CANARY"}'))
        self.assertIsNone(matrix.safe_detail('NATIVE_EXTERNAL {"cpu_ns":NaN}'))
        self.assertIsNotNone(matrix.safe_detail('NATIVE_EXTERNAL {"memory":{"memory.peak":123},"final":true}'))
        self.assertIsNotNone(matrix.safe_detail('NATIVE_MEMORY phase=p09.pass.after managedBytes=123 serverGC=False'))

    def test_required_ci_has_no_diagnostic_profile_entrypoint(self):
        workflow = (matrix.ROOT / ".github/workflows/ci.yml").read_text()
        self.assertNotIn("native-budget-ab", workflow)
        self.assertNotIn("continue-on-error", workflow)
        self.assertIn("name: native-runtime", workflow)
        resource = (matrix.ROOT / matrix.OVERLAYS[0]).read_text()
        self.assertNotIn("GetEnvironmentVariable", resource)
        runner = (matrix.ROOT / matrix.OVERLAYS[2]).read_text()
        self.assertIn("native-api) [[ $# == 0 ]]", runner)
        self.assertNotIn("NATIVE_AB_SAMPLE", runner)
        collector = (matrix.ROOT / matrix.OVERLAYS[3]).read_text()
        self.assertNotIn('add_argument("--memory', collector)

    def test_diagnostic_workflow_is_manual_only_without_reruns(self):
        workflow = (matrix.ROOT / ".github/workflows/native-budget-memory-ab.yml").read_text()
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertNotIn("  pull_request:", workflow)
        self.assertNotIn("name: native-runtime", workflow)
        self.assertIn('[[ "$GITHUB_RUN_ATTEMPT" == 1 ]]', workflow)
        self.assertIn("fail-fast: false", workflow)
        self.assertIn("if: always()", workflow)
        self.assertIn("contents: read", workflow)


if __name__ == "__main__":
    unittest.main()
