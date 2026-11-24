"""Portable fail-closed contract tests. No Docker/native/DB/hosted workloads."""

import importlib.util
import json
import os
import re
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("native_evidence", Path(__file__).with_name("native-runtime-evidence.py"))
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


def resource_line(stage="teardown-complete", **changes):
    values = dict.fromkeys(evidence.RESOURCE_KEYS, 0)
    values.update(memoryMax=evidence.MEMORY, peak=evidence.THRESHOLD, threshold=evidence.THRESHOLD)
    values.update(changes)
    return "NATIVE_RESOURCE stage=" + stage + " " + " ".join(f"{k}={values[k]}" for k in sorted(values))


def complete_lines():
    return ["IMAGE TESTS passed=94 failed=0",
            "RESOURCE pipelines=2 operations=60 JPEG=3020130 progressive=2781404 PNG=4156060 elapsedMs=1 processPeakBytes=1",
            evidence.NEGATIVE, evidence.NEGATIVE, resource_line("startup"),
            *[f"PASS {n}: {label}" for n, label in enumerate(evidence.pass_manifest(), 1)],
            "RESULT cases=18 elapsedMs=360000 processCpuMs=1 providerWrites=29 providerReads=31 providerDeletes=2 providerCancellations=5",
            resource_line(),
            "NATIVE_CONTAINER exit=0 oomKilled=false memory=805306368 memorySwap=805306368 nanoCpus=2000000000 pidsLimit=256"]


class EvidenceTests(unittest.TestCase):
    def test_below_and_exact_threshold_pass(self):
        for peak in (evidence.THRESHOLD - 1, evidence.THRESHOLD):
            lines = complete_lines()
            lines[-2] = resource_line(peak=peak)
            lines[4] = resource_line("startup", peak=1)
            self.assertTrue(evidence.assess(lines, 0)["accepted"])

    def test_every_invalid_resource_field_rejects(self):
        changes = [{"peak": evidence.THRESHOLD + 1}, {"peak": 0}, {"memoryMax": 536870912},
                   {"threshold": evidence.THRESHOLD + 1}]
        changes += [{key: 1} for key in evidence.RESOURCE_KEYS - {"peak", "memoryMax", "threshold"}]
        for change in changes:
            with self.subTest(change=change):
                lines = complete_lines()
                lines[-2] = resource_line(**change)
                self.assertFalse(evidence.assess(lines, 0)["accepted"])

    def test_all_missing_final_fields_fail(self):
        for key in evidence.RESOURCE_KEYS | {"stage"}:
            lines = complete_lines()
            lines[-2] = re.sub(r" " + key + r"=[^ ]+", "", lines[-2])
            self.assertFalse(evidence.assess(lines, 0)["accepted"])

    def test_missing_startup_final_and_container_reject(self):
        for position in (4, -2, -1):
            lines = complete_lines()
            lines.pop(position)
            self.assertFalse(evidence.assess(lines, 0)["accepted"])

    def test_invalid_duplicate_and_overflow_metrics(self):
        for invalid in ("max", "-1", "+1", "nan", "１２", "9223372036854775808"):
            lines = complete_lines()
            lines[-2] = lines[-2].replace("peak=671088640", "peak=" + invalid)
            self.assertFalse(evidence.assess(lines, 0)["accepted"])
        with self.assertRaises(ValueError):
            evidence.resource(resource_line() + " peak=1")

    def test_nonzero_exit_and_oom_do_not_pass(self):
        self.assertFalse(evidence.assess(complete_lines(), 137)["accepted"])
        for old, new in (("exit=0", "exit=137"), ("oomKilled=false", "oomKilled=true"),
                         ("memory=805306368", "memory=1073741824"),
                         ("memorySwap=805306368", "memorySwap=1073741824"),
                         ("nanoCpus=2000000000", "nanoCpus=3000000000"),
                         ("pidsLimit=256", "pidsLimit=512")):
            lines = complete_lines()
            lines[-1] = lines[-1].replace(old, new)
            self.assertFalse(evidence.assess(lines, 0)["accepted"])

    def test_each_missing_or_duplicate_coverage_record_rejects(self):
        for n in range(len(complete_lines())):
            for duplicate in (False, True):
                lines = complete_lines()
                if duplicate:
                    lines.insert(n, lines[n])
                else:
                    lines.pop(n)
                self.assertFalse(evidence.assess(lines, 0)["accepted"], (n, duplicate))

    def test_incorrect_workload_counts_reject(self):
        for old, new in (("passed=94", "passed=93"), ("operations=60", "operations=59"),
                         ("pipelines=2", "pipelines=1"), ("cases=18", "cases=17"),
                         ("providerWrites=29", "providerWrites=28")):
            lines = [line.replace(old, new) for line in complete_lines()]
            self.assertFalse(evidence.assess(lines, 0)["accepted"])

    def test_out_of_order_pass_or_final_before_teardown_reject(self):
        for a, b in ((5, 6), (4, -2), (-3, -2), (-2, -1)):
            lines = complete_lines()
            lines[a], lines[b] = lines[b], lines[a]
            self.assertFalse(evidence.assess(lines, 0)["accepted"])

    def test_privacy_allowlist(self):
        for line in complete_lines():
            self.assertEqual(line, evidence.safe_line(line, evidence.pass_manifest()))
        for line in ("secret SENSITIVE-CANARY", "Unhandled exception: SENSITIVE-CANARY", "NATIVE_RESOURCE_ERROR code=SENSITIVE-CANARY",
                     "NATIVE_CONTAINER secret=SENSITIVE-CANARY", "PASS 1: SENSITIVE-CANARY"):
            self.assertIsNone(evidence.safe_line(line, evidence.pass_manifest()))
        invalid = evidence.safe_line(resource_line() + " secret=SENSITIVE-CANARY", evidence.pass_manifest())
        self.assertNotIn("SENSITIVE-CANARY", invalid)

    def test_docker_inspection_never_requests_env(self):
        line = complete_lines()[-1]
        with patch.object(evidence.subprocess, "check_output", return_value=line) as call, patch("builtins.print"):
            self.assertEqual(0, evidence.inspect_container("owned-native"))
            self.assertNotIn("Config.Env", repr(call.call_args))
            self.assertIn(".HostConfig.MemorySwap", repr(call.call_args))
        with patch.object(evidence.subprocess, "check_output", return_value="SENSITIVE-CANARY"), patch("builtins.print") as output:
            self.assertEqual(1, evidence.inspect_container("owned-native"))
            self.assertNotIn("SENSITIVE-CANARY", repr(output.call_args))

    def test_collector_preserves_success_failure_and_redacts_unknown_output(self):
        for status in (0, 137):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                command = ["python3", "-c", "import sys; print(" + repr("\n".join(complete_lines()) + "\nSENSITIVE-CANARY") + f"); sys.exit({status})"]
                with patch("builtins.print"):
                    self.assertEqual(0 if status == 0 else 1, evidence.collect(root / "evidence", command))
                self.assertNotIn("SENSITIVE-CANARY", (root / "evidence/evidence.log").read_text())
                self.assertEqual(status, json.loads((root / "evidence/result.json").read_text())["exit_code"])


class RunnerTests(unittest.TestCase):
    def test_native_resources_exit_inspection_and_scoped_cleanup(self):
        script = Path(__file__).with_name("run-isolated.sh").resolve()
        for status in (0, 137, 256):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for name, text in {
                    "uname": '#!/bin/bash\ncase "$1" in -s) echo Linux;; -m) echo x86_64;; esac\n',
                    "getent": '#!/bin/bash\necho nogroup:x:65534:\n',
                    "docker": '''#!/usr/bin/env python3
import json, os, sys
with open(os.environ["NATIVE_TEST_CALLS"], "a") as f: f.write(json.dumps(sys.argv[1:]) + "\\n")
a=sys.argv[1]
if a == "run": print("owned-postgres")
elif a == "create": print("owned-native")
elif a == "wait": print(os.environ["NATIVE_TEST_EXIT"])
elif a == "inspect":
    if "NetworkMode" in sys.argv[3]: print("none")
    else: print("NATIVE_CONTAINER exit=" + os.environ["NATIVE_TEST_EXIT"] + " oomKilled=" + ("true" if os.environ["NATIVE_TEST_EXIT"] == "137" else "false") + " memory=805306368 memorySwap=805306368 nanoCpus=2000000000 pidsLimit=256")
''',
                }.items():
                    path = root / name
                    path.write_text(text)
                    path.chmod(0o755)
                env = {**os.environ, "PATH": str(root) + os.pathsep + os.environ["PATH"],
                       "NATIVE_TEST_CALLS": str(root / "calls"), "NATIVE_TEST_EXIT": str(status)}
                result = subprocess.run(["bash", str(script), "native-api"], env=env, text=True, capture_output=True, timeout=15)
                self.assertEqual(1 if status == 256 else status, result.returncode, result.stderr)
                calls = [json.loads(s) for s in (root / "calls").read_text().splitlines()]
                create = next(c for c in calls if c[0] == "create")
                for flag, value in (("--memory", "768m"), ("--memory-swap", "768m"), ("--cpus", "2"),
                                    ("--pids-limit", "256"), ("--network", "container:owned-postgres"),
                                    ("--cap-drop", "ALL"), ("--security-opt", "no-new-privileges"),
                                    ("--tmpfs", "/tmp:rw,noexec,nosuid,size=32m")):
                    self.assertEqual(value, create[create.index(flag) + 1])
                self.assertIn("--read-only", create)
                self.assertIn(["rm", "--force", "owned-native"], calls)
                self.assertIn(["stop", "owned-postgres"], calls)
                self.assertLess(next(i for i,c in enumerate(calls) if c[0] == "wait"),
                                next(i for i,c in enumerate(calls) if c[:3] == ["rm", "--force", "owned-native"]))


if __name__ == "__main__":
    unittest.main()
