"""Sanitization/reduction/protocol checks, not hosted A/B or native test substitutes."""
import importlib.util
import json
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("native_ab", Path(__file__).with_name("native-ab.py"))
ab = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ab)


class NativeAbTests(unittest.TestCase):
    def test_each_of_three_blocks_runs_all_four_conditions(self):
        expected = {"base-control", "base-instrumented", "head-control", "head-instrumented"}
        self.assertEqual(len(ab.ORDERS), 3)
        for order in ab.ORDERS.values():
            self.assertEqual(len(order), 4)
            self.assertEqual(set(order), expected)

    def test_unknown_output_and_sensitive_payloads_are_not_retained(self):
        for line in ["Authorization: Bearer synthetic-canary", "RESULT anything=synthetic-canary",
                     "PASS 9: synthetic-canary", 'NATIVE_EXTERNAL {"secret":"synthetic-canary"}',
                     'NATIVE_EXTERNAL {"utc":"https://synthetic-canary/"}',
                     "NATIVE_MEMORY phase=p09.failed-read.begin secret=123",
                     "Unhandled exception. BadException: https://synthetic-canary/?token=fake"]:
            with self.subTest(line=line):
                result = ab.sanitize(line, {"known case"})
                self.assertNotIn("synthetic-canary", result or "")
                self.assertNotIn("https://", result or "")

    def test_fixed_labels_and_numeric_metrics_survive(self):
        self.assertEqual(ab.sanitize("PASS 9: known case", {"known case"}), "PASS 9: known case")
        line = "NATIVE_MEMORY utc=2026-11-23T12:00:00+00:00 sample=1 phase=p09.failed-read.begin cgroupPeak=490426368 processPeakBytes=582553600"
        self.assertEqual(ab.sanitize(line, set()), line)
        external = 'NATIVE_EXTERNAL {"sample":1,"final":true,"member_count":0,"memory":{"memory.peak":536870912},"events":{"oom_kill":1}}'
        self.assertIsNotNone(ab.sanitize(external, set()))

    def test_oom_result_is_not_promoted_to_success(self):
        lines = ["PASS 8: known case", "NATIVE_MEMORY sample=1 phase=p09.recovered-read.begin cgroupPeak=490426368 processPeakBytes=582553600",
                 "Native harness: startedAt=2026-11-23T12:00:00Z finishedAt=2026-11-23T12:01:00Z image=sha256:" + "a" * 64 + " exit=137 oomKilled=true memory=536870912 memorySwap=536870912 nanoCpus=2000000000 pidsLimit=256"]
        result = ab.summarize(lines, 137)
        self.assertTrue(result["oom_killed"])
        self.assertEqual(result["native_exit"], 137)
        self.assertEqual(result["last_pass"], 8)
        self.assertEqual(result["last_phase"], "p09.recovered-read.begin")
        self.assertEqual(result["cgroup_peak_observed"], 490426368)
        self.assertFalse(result["peak_is_exit_complete"])
        self.assertFalse(result["complete_workload_pass"])

    def test_full_workload_requires_every_gate_not_only_exit_zero(self):
        self.assertFalse(ab.summarize([], 0)["complete_workload_pass"])
        lines = ["PASS 18: known", "IMAGE TESTS passed=94 failed=0",
                 "RESOURCE pipelines=2 operations=60 JPEG=1", "RESULT cases=18 elapsedMs=1",
                 "PASS missing/tampered native asset fails unavailable without decoder fallback."]
        lines += [lines[-1], "Native harness: exit=0 oomKilled=false"]
        self.assertTrue(ab.summarize(lines, 0)["complete_workload_pass"])
        self.assertFalse(ab.summarize(lines[:-1], 0)["complete_workload_pass"])

    def test_final_external_peak_is_distinguished_from_checkpoint_peak(self):
        fields = {"final": True, "member_count": 0, "memory": {"memory.peak": 500000000}}
        result = ab.summarize(["NATIVE_EXTERNAL " + json.dumps(fields)], 0)
        self.assertTrue(result["peak_is_exit_complete"])
        self.assertEqual(result["cgroup_peak_observed"], 500000000)


if __name__ == "__main__":
    unittest.main()
