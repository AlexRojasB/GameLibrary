"""Frozen diagnostic matrix preparation/execution. NOT required-CI acceptance.

Default --plan performs no workloads. Execution needs explicit approval opt-in and
an exact clean checkout SHA. Only disposable worktrees receive diagnostic overlays.
"""

import argparse
import datetime
import hashlib
import importlib.util
import json
import os
import platform
import re
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = "503766763fed33a839d37f4107dcd8d1b1a3c59c"
INSTRUMENT = "a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597"
APPROVAL = "EXECUTE_FROZEN_18_WORKLOAD_MATRIX"
ORDERS = {
    1: ["512-minimal", "768-detailed", "1024-minimal", "512-detailed", "768-minimal", "1024-detailed"],
    2: ["1024-detailed", "768-minimal", "512-detailed", "1024-minimal", "768-detailed", "512-minimal"],
    3: ["768-minimal", "1024-detailed", "512-minimal", "768-detailed", "1024-minimal", "512-detailed"],
}
OVERLAYS = ("tools/cover-api/NativeResourceBudget.cs", "tools/cover-api/Program.cs",
            "tools/ci/run-isolated.sh", "tools/ci/native-runtime-evidence.py")
MEMORY_KEYS = set("utc sample phase managedBytes allocatedBytes gcHeapBytes gcCommittedBytes gcFragmentedBytes gcAvailableBytes gcMemoryLoadBytes serverGC gen0 gen1 gen2 workingBytes privateBytes processPeakBytes diagnosticPriorThreadAllocatedBytes diagnosticPriorElapsedTicks stopwatchFrequency cgroupCurrent cgroupPeak cgroupMax cgroupEvents".split())
EXTERNAL_KEYS = set("utc monotonic_ns memory memory.current memory.peak memory.max events low high max oom oom_kill oom_group_kill stat anon file kernel kernel_stack pagetables sock shmem slab file_mapped member_count truncated processes pid status Pid PPid VmRSS VmHWM VmSize VmData VmSwap RssAnon RssFile RssShmem Threads smaps_rollup Rss Pss Pss_Anon Pss_File Pss_Shmem Private_Clean Private_Dirty Shared_Clean Shared_Dirty Anonymous Swap sample final stop_reason state reason samples elapsed_ns cpu_ns sampler_peak_rss_kib interval_seconds".split())
EXTERNAL_STRINGS = {"unavailable", "target-cgroup-not-found", "target-exited-before-sampling", "stop-requested",
                    "target-empty", "cgroup-unavailable", "sampler-finished"}


def git(*args, cwd=ROOT):
    return subprocess.check_output(["git", *args], cwd=cwd, text=True).strip()


def blob(revision, path):
    return subprocess.check_output(["git", "show", f"{revision}:{path}"], cwd=ROOT).decode()


def once(text, old, new):
    if text.count(old) != 1:
        raise ValueError("Frozen overlay no longer matches; review required.")
    return text.replace(old, new)


def safe_external(value):
    if value is None or type(value) in (bool, int):
        return True
    if type(value) is float:
        return value >= 0 and value < 1e30
    if isinstance(value, str):
        return value in EXTERNAL_STRINGS or bool(re.fullmatch(r"[0-9]{4}-[0-9T:.+Z-]+", value))
    if isinstance(value, list):
        return len(value) <= 256 and all(safe_external(v) for v in value)
    return isinstance(value, dict) and all(k in EXTERNAL_KEYS and safe_external(v) for k, v in value.items())


def safe_detail(line):
    line = line.strip()
    if len(line) > 65536:
        return None
    if line.startswith("NATIVE_EXTERNAL "):
        try:
            value = json.loads(line.removeprefix("NATIVE_EXTERNAL "))
            return "NATIVE_EXTERNAL " + json.dumps(value, separators=(",", ":")) if isinstance(value, dict) and safe_external(value) else None
        except ValueError:
            return None
    if line.startswith("NATIVE_MEMORY "):
        parts = [part.split("=", 1) for part in line.split()[1:]]
        if any(len(p) != 2 for p in parts) or len(dict(parts)) != len(parts):
            return None
        for key, value in parts:
            if key not in MEMORY_KEYS:
                return None
            if key == "utc":
                valid = re.fullmatch(r"[0-9]{4}-[0-9T:.+Z-]+", value)
            elif key == "phase":
                valid = re.fullmatch(r"(?:p[0-9]{2}\.[a-z.-]+|fixtures\.[a-z.-]+|final|overlap-two-uploads-eight-2MiB-reads)", value)
            elif key == "cgroupEvents":
                valid = re.fullmatch(r"(?:(?:low|high|max|oom|oom_kill|oom_group_kill):[0-9]+,?)+|unavailable", value)
            else:
                valid = re.fullmatch(r"[0-9]+|True|False|unavailable", value)
            if not valid:
                return None
        return line
    if re.fullmatch(r"NATIVE_DIAGNOSTIC_COST samples=[0-9]+ threadAllocatedBytes=[0-9]+ elapsedTicks=[0-9]+ stopwatchFrequency=[0-9]+", line):
        return line
    return None


def overlay(files, budget, detailed):
    if budget not in (512, 768, 1024):
        raise ValueError("Unapproved diagnostic profile.")
    files = dict(files)
    files[OVERLAYS[0]] = once(files[OVERLAYS[0]], "MemoryLimitBytes = 768L", f"MemoryLimitBytes = {budget}L")
    files[OVERLAYS[2]] = once(files[OVERLAYS[2]], "--memory 768m --memory-swap 768m", f"--memory {budget}m --memory-swap {budget}m")
    files[OVERLAYS[3]] = once(files[OVERLAYS[3]], "MEMORY = 805306368", f"MEMORY = {budget * 1048576}")
    if not detailed:
        return files
    # Reuse the exact reviewed snapshot writer/sampler; do not edit historical protocols.
    historical = blob(INSTRUMENT, "tools/cover-api/Program.cs")
    writer = historical[historical.index("// Diagnostic values only:"):]
    program = "using System.Globalization;\n" + files[OVERLAYS[1]]
    old_metrics = program[program.index("        static void Metrics(string phase)"):program.index("\n    }\n}\n\n// GetAsync")]
    program = once(program, old_metrics, "        static void Metrics(string phase) => NativeMemoryDiagnostics.Write(phase);")
    program = once(program, 'void Check(string name) { cases++; Console.WriteLine($"PASS {cases}: {name}"); }',
                   'void Check(string name) { cases++; Console.WriteLine($"PASS {cases}: {name}"); Metrics($"p{cases:00}.pass.after"); }')
    insertions = {
        "        await using var database =": '        Metrics("p00.setup.begin");\n',
        "        var seed =": '        Metrics("fixtures.seed.begin");\n',
        "        var baseline =": '        Metrics("fixtures.generate.begin");\n',
        "        foreach (var fixture in": '        Metrics("fixtures.generate.end");\n',
        "            provider.ReadsBlocked = StorageFixture.Block();\n            var reads =": '            Metrics($"p{cases + 1:00}.overlap.begin");\n',
        "            using var timingClient =": '            Metrics(write ? "p08.write-timeout.begin" : "p07.read-timeout.begin");\n',
        "        provider.FailReads = true;": '        Metrics("p09.failed-read.begin");\n',
        "        provider.FailReads = false;": '        Metrics("p09.failed-read.end");\n        Metrics("p09.recovered-read.begin");\n',
        "        provider.Public = true;": '        Metrics("p09.recovered-read.end");\n        Metrics("p09.public-bucket.begin");\n',
        "        provider.Public = false;": '        Metrics("p09.public-bucket.end");\n',
        "        byte[] tiny;": '        Metrics("p10.rate-windows.begin");\n',
    }
    for key, prefix in insertions.items():
        program = once(program, key, prefix + key)
    files[OVERLAYS[1]] = program + "\n" + writer
    runner = files[OVERLAYS[2]]
    runner = once(runner, "native_container=''", "native_container=''\nnative_sampler=''\nnative_diagnostics=''")
    runner = once(runner, "  trap - EXIT", """  trap - EXIT
  if [[ -n "$native_sampler" ]]; then
    touch "$native_diagnostics/stop"
    wait "$native_sampler" || true
    rm -f -- "$native_diagnostics/stop"
    rmdir -- "$native_diagnostics" || true
  fi""")
    runner = once(runner, '    docker start --attach "$native_container" || attach_status=$?', """    native_diagnostics=$(mktemp -d "${RUNNER_TEMP:-/tmp}/native-budget-observer.XXXXXX")
    docker start "$native_container" >/dev/null
    native_pid=$(docker inspect --format '{{.State.Pid}}' "$native_container")
    [[ "$native_pid" =~ ^[1-9][0-9]*$ ]] || exit 1
    sudo -n python3 -u "$(dirname "$script")/native-memory-sampler.py" --pid "$native_pid" --stop-file "$native_diagnostics/stop" &
    native_sampler=$!
    docker logs --follow "$native_container" || attach_status=$?""")
    files[OVERLAYS[2]] = runner
    return files


def plan():
    return {"kind": "diagnostic-only", "workloads": 18, "threshold_bytes": 671088640,
            "application_base": BASE, "instrumentation": INSTRUMENT,
            "orders": ORDERS, "required_acceptance": "fixed-768MiB-640MiB-no-overrides"}


def execute(block, output, expected_revision, approval):
    if approval != APPROVAL or not re.fullmatch(r"[a-f0-9]{40}", expected_revision):
        raise ValueError("Explicit approved execution and exact SHA required.")
    if platform.system() != "Linux" or platform.machine() != "x86_64" or os.geteuid() == 0:
        raise ValueError("Actual non-root Linux amd64 required.")
    if git("rev-parse", "HEAD") != expected_revision or git("status", "--porcelain"):
        raise ValueError("Expected clean frozen source checkout.")
    git("diff", "--exit-code", BASE, expected_revision, "--", "src", "tests", "tools/cover-native")
    output = output.resolve()
    if output.is_relative_to(ROOT):
        raise ValueError("Evidence must be outside the source checkout.")
    output.mkdir(parents=True, exist_ok=False)
    context = {**plan(), "source_revision": expected_revision, "block": block,
               "environment": {"kernel": platform.release(), "architecture": platform.machine()},
               "trials": []}
    manifest = output / "manifest.json"
    manifest.write_text(json.dumps(context, indent=2))
    cpu = json.loads(subprocess.check_output(["lscpu", "-J"], text=True))
    context["environment"]["cpu"] = {row["field"]: row["data"] for row in cpu["lscpu"]
                                      if row["field"] in {"Architecture:", "CPU(s):", "Vendor ID:", "Model name:", "Hypervisor vendor:"}}
    context["environment"]["docker"] = subprocess.check_output(
        ["docker", "info", "--format", "{{.ServerVersion}}|{{.CgroupVersion}}|{{.NCPU}}"], text=True).strip()
    source = {path: blob(expected_revision, path) for path in OVERLAYS}
    context["source_sha256"] = {p: hashlib.sha256(s.encode()).hexdigest() for p, s in source.items()}
    context["pinned_build_sha256"] = {p: hashlib.sha256(blob(expected_revision, p).encode()).hexdigest()
                                       for p in ("tools/cover-api/Dockerfile", "tools/cover-native/Dockerfile", "tools/cover-native/prepare.sh", "tools/ci/postgres-image.txt")}
    for order, condition in enumerate(ORDERS[block], 1):
        budget, mode = condition.split("-")
        trial_id = f"block-{block}-{order}-{condition}"
        tree = output.parent / ("source-" + trial_id)
        trial = {"id": trial_id, "budget_mib": int(budget), "mode": mode, "complete": False}
        trial["started_utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        started = time.monotonic_ns()
        context["trials"].append(trial)
        manifest.write_text(json.dumps(context, indent=2))
        try:
            git("worktree", "add", "--detach", str(tree), expected_revision)
            changed = overlay(source, int(budget), mode == "detailed")
            if mode == "detailed":
                changed["tools/ci/native-memory-sampler.py"] = blob(INSTRUMENT, "tools/ci/native-memory-sampler.py")
            for path, text in changed.items():
                (tree / path).write_text(text)
            trial["overlay_sha256"] = {p: hashlib.sha256(s.encode()).hexdigest() for p, s in changed.items()}
            # Import this trial's exact resource constant; no required-CI CLI profile switch.
            spec = importlib.util.spec_from_file_location("trial_evidence", tree / OVERLAYS[3])
            evidence = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(evidence)
            status = evidence.collect(output / trial_id, safe_extra=safe_detail if mode == "detailed" else None)
            trial["result"] = json.loads((output / trial_id / "result.json").read_text())
            log = (output / trial_id / "evidence.log").read_text()
            external = [json.loads(line.removeprefix("NATIVE_EXTERNAL ")) for line in log.splitlines()
                        if line.startswith("NATIVE_EXTERNAL ")]
            trial["observer_present"] = mode == "minimal" or (
                "NATIVE_MEMORY " in log and "NATIVE_DIAGNOSTIC_COST " in log
                and any(isinstance(record.get("memory"), dict)
                        and type(record["memory"].get("memory.peak")) is int for record in external)
                and any(record.get("state") == "sampler-finished" for record in external))
            trial["accepted"] = status == 0 and trial["observer_present"]
            trial["complete"] = True
        except (OSError, ValueError, subprocess.SubprocessError):
            trial["accepted"] = False
            trial["error"] = "diagnostic-trial-failed-details-withheld"
        trial["finished_utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        trial["elapsed_ns_including_build"] = time.monotonic_ns() - started
        manifest.write_text(json.dumps(context, indent=2))
    # Keep every failure and every disposable source tree for analysis; no retries.
    return 0 if all(t.get("accepted") for t in context["trials"]) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", action="store_true")
    parser.add_argument("--block", type=int, choices=ORDERS)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--expected-revision")
    parser.add_argument("--approval")
    args = parser.parse_args()
    if args.plan or args.block is None:
        print(json.dumps(plan(), indent=2))
    elif not args.output or not args.expected_revision or not args.approval:
        parser.error("Execution requires output, exact expected revision and explicit approval.")
    else:
        raise SystemExit(execute(args.block, args.output, args.expected_revision, args.approval))
