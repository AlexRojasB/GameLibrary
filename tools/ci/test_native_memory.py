"""Portable diagnostic tests. Mock proc/cgroup/Docker; NOT Linux/OOM evidence."""

import importlib.util
import io
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("native_memory", Path(__file__).with_name("native-memory-sampler.py"))
sampler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sampler)


class NativeMemoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.proc = self.root / "proc"
        self.cgroup = self.root / "cgroup"
        self.group = self.cgroup / "owned"
        self.group.mkdir(parents=True)
        for name, value in {"cgroup.procs": "101\n", "memory.current": "1234", "memory.peak": "5678",
                            "memory.max": "536870912", "memory.events": "oom 0\noom_kill 0\n",
                            "memory.stat": "anon 123\nfile 456\n"}.items():
            (self.group / name).write_text(value)
        self.member(101, "/owned")
        for key, value in [("PROC", self.proc), ("CGROUP", self.cgroup)]:
            mock = patch.object(sampler, key, value)
            mock.start()
            self.addCleanup(mock.stop)

    def member(self, pid, group):
        process = self.proc / str(pid)
        process.mkdir(parents=True, exist_ok=True)
        (process / "cgroup").write_text("0::" + group + "\n")
        (process / "status").write_text(f"Pid:\t{pid}\nVmRSS:\t12 kB\nThreads:\t3\nName:\tSENSITIVE-CANARY\n")
        (process / "smaps_rollup").write_text("1000-2000 rw-p [rollup]\nRss: 12 kB\nPrivate_Dirty: 4 kB\n")

    def test_only_numeric_allowlists_are_reported_and_kib_becomes_bytes(self):
        (self.group / "memory.stat").write_text("anon 42\nfile not-a-number\nsecret SENSITIVE-CANARY\n")
        result = sampler.sample(self.group)
        self.assertEqual(result["stat"], {"anon": 42})
        self.assertEqual(result["processes"][0]["status"]["VmRSS"], 12288)
        self.assertEqual(result["processes"][0]["status"]["Threads"], 3)
        self.assertEqual(result["processes"][0]["smaps_rollup"]["Private_Dirty"], 4096)
        self.assertNotIn("SENSITIVE-CANARY", json.dumps(result))
        self.assertNotIn("1000-2000", json.dumps(result))

    def test_sampler_does_not_scan_other_cgroup_members_or_reused_pids(self):
        self.member(102, "/other")
        (self.group / "cgroup.procs").write_text("101\n102\n")
        self.assertEqual([p["pid"] for p in sampler.sample(self.group)["processes"]], [101])

    def test_root_and_escaping_cgroups_are_rejected(self):
        for value in ["/", "/../../outside"]:
            with self.subTest(value=value):
                self.member(101, value)
                self.assertIsNone(sampler.locate(101))

    def test_missing_or_v1_membership_is_unavailable(self):
        self.assertIsNone(sampler.locate(999))
        (self.proc / "101/cgroup").write_text("5:memory:/owned\n")
        self.assertIsNone(sampler.locate(101))

    def test_missing_memory_is_not_a_zero_measurement(self):
        (self.group / "memory.current").unlink()
        self.assertIsNone(sampler.sample(self.group)["memory"]["memory.current"])

    def test_excessive_files_and_non_ascii_numbers_are_rejected(self):
        (self.group / "memory.stat").write_text("x" * 65537)
        self.assertIsNone(sampler.counters(self.group / "memory.stat", sampler.MEMORY))
        self.assertIsNone(sampler.number("１２３"))
        self.assertIsNone(sampler.number("1" * 65536))

    def test_monitor_emits_final_oom_sample_after_target_disappears(self):
        def terminate(_):
            (self.group / "cgroup.procs").write_text("")
            (self.group / "memory.events").write_text("oom 1\noom_kill 1\n")
        output = io.StringIO()
        with patch.object(sampler.time, "sleep", side_effect=terminate), patch("sys.stdout", output):
            sampler.monitor(101, self.root / "stop")
        records = [json.loads(line.removeprefix("NATIVE_EXTERNAL ")) for line in output.getvalue().splitlines()]
        self.assertFalse(records[0]["final"])
        self.assertTrue(records[1]["final"])
        self.assertEqual(records[1]["events"]["oom_kill"], 1)
        self.assertEqual(records[1]["member_count"], 0)
        self.assertEqual(records[1]["stop_reason"], "target-empty")
        self.assertEqual(records[2]["state"], "sampler-finished")

    def test_stop_requests_a_last_sample_before_cleanup(self):
        stop = self.root / "stop"
        stop.touch()
        output = io.StringIO()
        with patch("sys.stdout", output):
            sampler.monitor(101, stop)
        first = json.loads(output.getvalue().splitlines()[0].removeprefix("NATIVE_EXTERNAL "))
        self.assertTrue(first["final"])
        self.assertEqual(first["stop_reason"], "stop-requested")
        self.assertEqual(first["memory"]["memory.peak"], 5678)

    def test_unavailable_target_reports_no_paths_or_exception_text(self):
        output = io.StringIO()
        with patch("sys.stdout", output):
            sampler.monitor(999, self.root / "stop")
        self.assertEqual(json.loads(output.getvalue().removeprefix("NATIVE_EXTERNAL ")),
                         {"state": "unavailable", "reason": "target-cgroup-not-found"})


class NativeRunnerExitTests(unittest.TestCase):
    def test_success_and_oom_exit_codes_survive_sampling_and_cleanup(self):
        if os.geteuid() == 0:
            self.fail("These runner tests require the same non-root user as the workflow.")
        script = Path(__file__).with_name("run-isolated.sh").resolve()
        for code in (0, 137):
            with self.subTest(code=code), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                calls = root / "calls"
                for name, content in {
                    "uname": '#!/bin/bash\ncase "$1" in -s) echo Linux;; -m) echo x86_64;; -r) echo synthetic-kernel;; esac\n',
                    "getent": '#!/bin/bash\necho nogroup:x:65534:\n',
                    "sudo": '#!/bin/bash\nexit 0\n',
                    "docker": '''#!/usr/bin/env python3
import json, os, sys
with open(os.environ["NATIVE_TEST_CALLS"], "a") as log: log.write(json.dumps(sys.argv[1:]) + "\\n")
action = sys.argv[1]
if action == "run": print("owned-postgres")
elif action == "create": print("owned-native")
elif action == "wait": print(os.environ["NATIVE_TEST_EXIT"])
elif action == "inspect":
    if "NetworkMode" in sys.argv[3]: print("none")
    elif "State.Pid" in sys.argv[3]: print("101")
    else: print("Native harness: exit=" + os.environ["NATIVE_TEST_EXIT"])
''',
                }.items():
                    path = root / name
                    path.write_text(content)
                    path.chmod(0o755)
                env = {**os.environ, "PATH": str(root) + os.pathsep + os.environ["PATH"],
                       "RUNNER_TEMP": temp, "NATIVE_TEST_CALLS": str(calls), "NATIVE_TEST_EXIT": str(code)}
                result = subprocess.run(["bash", str(script), "native-api"], env=env, capture_output=True, text=True, timeout=15)
                self.assertEqual(result.returncode, code, result.stderr)
                commands = [json.loads(line) for line in calls.read_text().splitlines()]
                create = next(command for command in commands if command[0] == "create")
                for flag, value in [("--memory", "512m"), ("--memory-swap", "512m"), ("--cpus", "2"),
                                    ("--pids-limit", "256"), ("--network", "container:owned-postgres"),
                                    ("--cap-drop", "ALL"), ("--security-opt", "no-new-privileges")]:
                    self.assertEqual(create[create.index(flag) + 1], value)
                self.assertIn("--read-only", create)
                self.assertIn(["rm", "--force", "owned-native"], commands)
                self.assertIn(["stop", "owned-postgres"], commands)
                self.assertFalse(list(root.glob("native-memory.*")))


if __name__ == "__main__":
    unittest.main()
