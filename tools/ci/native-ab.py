"""Fixed, diagnostic-only source x observer experiment. Never run production IO.

All trial output is allowlisted before console/artifact storage. Build chatter and
unknown exception details are counted, not retained. Test exits remain failures.
"""

import argparse
import datetime
import hashlib
import json
import os
import platform
import re
import subprocess
import time
from pathlib import Path

BASE = "503766763fed33a839d37f4107dcd8d1b1a3c59c"
HEAD = "5483b200a8d960d9c2b99cf15e04448283059466"
INSTRUMENT = "a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597"
ORDERS = {
    1: ["base-control", "base-instrumented", "head-instrumented", "head-control"],
    2: ["head-instrumented", "head-control", "base-control", "base-instrumented"],
    3: ["base-instrumented", "head-control", "base-control", "head-instrumented"],
}
PROGRAM = "tools/cover-api/Program.cs"
RUNNER = "tools/ci/run-isolated.sh"
SAMPLER = "tools/ci/native-memory-sampler.py"
REPO = Path(__file__).resolve().parents[2]
ANSI = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")
PHASE = r"(?:p\d{2}\.[a-z.-]+|fixtures\.[a-z.-]+|final|overlap-two-uploads-eight-2MiB-reads)"
SAFE_EXTERNAL_KEYS = set("utc monotonic_ns memory memory.current memory.peak memory.max events low high max oom oom_kill oom_group_kill stat anon file kernel kernel_stack pagetables sock shmem slab file_mapped member_count truncated processes pid status Pid PPid VmRSS VmHWM VmSize VmData VmSwap RssAnon RssFile RssShmem Threads smaps_rollup Rss Pss Pss_Anon Pss_File Pss_Shmem Private_Clean Private_Dirty Shared_Clean Shared_Dirty Anonymous Swap sample final stop_reason state reason samples elapsed_ns cpu_ns sampler_peak_rss_kib interval_seconds".split())
SAFE_EXTERNAL_STRINGS = {"unavailable", "target-cgroup-not-found", "target-exited-before-sampling",
                         "observer-disabled-control", "stop-requested", "target-empty", "cgroup-unavailable", "sampler-finished"}
MEMORY_KEYS = set("utc sample phase managedBytes allocatedBytes gcHeapBytes gcCommittedBytes gcFragmentedBytes gcAvailableBytes gcMemoryLoadBytes serverGC gen0 gen1 gen2 workingBytes privateBytes processPeakBytes diagnosticPriorThreadAllocatedBytes diagnosticPriorElapsedTicks stopwatchFrequency cgroupCurrent cgroupPeak cgroupMax cgroupEvents".split())


def git(*args, cwd=REPO):
    return subprocess.check_output(["git", *args], cwd=cwd, text=True).strip()


def blob(revision, path):
    return subprocess.check_output(["git", "show", f"{revision}:{path}"], cwd=REPO)


def utc(value):
    try:
        datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
        return bool(re.fullmatch(r"[0-9T:.+Z-]+", value))
    except ValueError:
        return False


def safe_external(value):
    if value is None or type(value) in (int, bool, float):
        return True
    if isinstance(value, str):
        return value in SAFE_EXTERNAL_STRINGS or utc(value)
    if isinstance(value, list):
        return len(value) <= 256 and all(safe_external(item) for item in value)
    if isinstance(value, dict):
        return all(key in SAFE_EXTERNAL_KEYS and safe_external(item) for key, item in value.items())
    return False


def sanitize(line, pass_labels):
    line = ANSI.sub("", line.strip())
    if len(line) > 65536:
        return None
    if line.startswith("NATIVE_EXTERNAL "):
        try:
            value = json.loads(line.removeprefix("NATIVE_EXTERNAL "))
            return "NATIVE_EXTERNAL " + json.dumps(value, separators=(",", ":")) if safe_external(value) else None
        except json.JSONDecodeError:
            return None
    if line.startswith("NATIVE_MEMORY "):
        pairs = line.split()[1:]
        for pair in pairs:
            key, sep, value = pair.partition("=")
            if not sep or key not in MEMORY_KEYS:
                return None
            if key == "utc":
                if not utc(value): return None
            elif key == "phase":
                if not re.fullmatch(PHASE, value): return None
            elif key == "cgroupEvents":
                if not re.fullmatch(r"(?:(?:low|high|max|oom|oom_kill|oom_group_kill):\d+,?)*|unavailable", value): return None
            elif not re.fullmatch(r"\d+|True|False|unavailable", value):
                return None
        return line
    if line.startswith("PASS "):
        match = re.fullmatch(r"PASS (\d+): (.+)", line)
        if match and match[2] in pass_labels:
            return line
        return line if line == "PASS missing/tampered native asset fails unavailable without decoder fallback." else None
    patterns = [
        r"(?:TARGET runtime|Runtime)=\.NET [\d.]+ (?:os|OS)=Ubuntu [\d.]+ LTS arch=X64 user=app",
        r"FIXTURES baseline=\d+ progressive=\d+ png=\d+ JPEGpixels=\d+ proxyBytes=\d+ uploadBytes=\d+",
        r"KESTREL minimumBodyBytesPerSecond=[\d.]+ graceSeconds=[\d.]+ shutdownSeconds=[\d.]+",
        rf"METRICS phase={PHASE} workingBytes=\d+ processPeakBytes=\d+",
        rf"CGROUP {PHASE} memory\.(?:current|peak|max)=\d+",
        rf"CGROUP {PHASE} memory.events=(?:(?:low|high|max|oom|oom_kill|oom_group_kill|sock_throttled) \d+;?)+",
        r"TIMEOUT operation=(?:read|write) elapsedMs=\d+",
        r"RATE (?:batch=\d+ accepted=\d+ minuteRetrySeconds=\d+|hourlyAccepted=\d+ hourRetrySeconds=\d+)",
        r"RECEIVE pulses=(?:True|False) elapsedMs=\d+ status=408",
        r"SHUTDOWN activeProviderRead elapsedMs=\d+",
        r"RESULT cases=\d+ elapsedMs=\d+ processCpuMs=\d+ providerWrites=\d+ providerReads=\d+ providerDeletes=\d+ providerCancellations=\d+",
        r"RESOURCE pipelines=\d+ operations=\d+ JPEG=\d+ progressive=\d+ PNG=\d+ elapsedMs=\d+ processPeakBytes=\d+",
        r"IMAGE TESTS passed=\d+ failed=\d+",
        r"NATIVE_DIAGNOSTIC_COST samples=\d+ threadAllocatedBytes=\d+ elapsedTicks=\d+ stopwatchFrequency=\d+",
        r"/sys/fs/cgroup/(?:memory\.peak|memory\.max): \d+",
        r"(?:low|high|max|oom|oom_kill|oom_group_kill|sock_throttled) \d+",
        r"NATIVE_TRIAL utc=[0-9TZ:+.-]+ checkout=[a-f0-9]{40} kernel=[A-Za-z0-9.+_-]+ arch=x86_64",
        r"Native harness: startedAt=[0-9TZ:+.-]+ finishedAt=[0-9TZ:+.-]+ image=sha256:[a-f0-9]{64} exit=\d+ oomKilled=(?:true|false) memory=536870912 memorySwap=536870912 nanoCpus=2000000000 pidsLimit=256",
    ]
    # These prefixes are numeric/fixed test diagnostics, never arbitrary error data.
    if any(re.fullmatch(pattern, line) for pattern in patterns):
        if any(word in line.lower() for word in ("token", "cookie", "password", "secret", "authorization", "http")):
            return None
        return line
    exception = re.match(r"Unhandled exception\. ([A-Za-z0-9_.]+Exception):", line)
    return "TEST_EXCEPTION type=" + exception[1] + " details=withheld" if exception else None


def summarize(lines, exit_code):
    result = {"exit_code": exit_code, "native_exit": None, "oom_killed": None, "last_pass": 0,
              "last_phase": None, "cgroup_peak_observed": None, "process_peak_observed": None,
              "peak_is_exit_complete": False, "timeline_08_09": [], "gc_snapshots": [],
              "observer_cost": None, "diagnostic_cost": None, "native_container": None,
              "runtime": None, "native_duration_seconds": None}
    peaks, process_peaks = [], []
    for line in lines:
        if match := re.match(r"PASS (\d+):", line):
            result["last_pass"] = int(match[1])
        if line.startswith("NATIVE_MEMORY "):
            fields = dict(pair.split("=", 1) for pair in line.split()[1:])
            result["last_phase"] = fields["phase"]
            result["gc_snapshots"].append(fields)
            if fields["phase"].startswith(("p08.", "p09.")):
                result["timeline_08_09"].append(fields)
            if fields["cgroupPeak"].isdigit(): peaks.append(int(fields["cgroupPeak"]))
            if fields["processPeakBytes"].isdigit(): process_peaks.append(int(fields["processPeakBytes"]))
        if line.startswith("NATIVE_EXTERNAL "):
            fields = json.loads(line.removeprefix("NATIVE_EXTERNAL "))
            peak = fields.get("memory", {}).get("memory.peak")
            if isinstance(peak, int):
                peaks.append(peak)
                if fields.get("final") and fields.get("member_count") == 0:
                    result["peak_is_exit_complete"] = True
            if fields.get("state") == "sampler-finished": result["observer_cost"] = fields
        if line.startswith("CGROUP ") and (match := re.search(r"memory.peak=(\d+)", line)):
            peaks.append(int(match[1]))
        if line.startswith("METRICS ") and (match := re.search(r"processPeakBytes=(\d+)", line)):
            process_peaks.append(int(match[1]))
        if line.startswith("Native harness: "):
            result["native_container"] = line
            result["native_exit"] = int(re.search(r"exit=(\d+)", line)[1])
            result["oom_killed"] = "oomKilled=true" in line
            dates = re.search(r"startedAt=([^ ]+) finishedAt=([^ ]+)", line)
            if dates:
                start = datetime.datetime.fromisoformat(dates[1].replace("Z", "+00:00"))
                end = datetime.datetime.fromisoformat(dates[2].replace("Z", "+00:00"))
                result["native_duration_seconds"] = (end - start).total_seconds()
        if line.startswith("TARGET runtime="): result["runtime"] = line
        if line.startswith("NATIVE_DIAGNOSTIC_COST "): result["diagnostic_cost"] = line
    result["cgroup_peak_observed"] = max(peaks, default=None)
    result["process_peak_observed"] = max(process_peaks, default=None)
    result["complete_workload_pass"] = (exit_code == 0 and result["native_exit"] == 0
        and result["last_pass"] == 18 and any("IMAGE TESTS passed=94 failed=0" in line for line in lines)
        and any("RESOURCE pipelines=2 operations=60 " in line for line in lines)
        and sum(line.startswith("PASS missing/tampered native") for line in lines) == 2
        and any(line.startswith("RESULT cases=18 ") for line in lines))
    return result


def environment():
    # No env dump, hostnames, Docker config, connection strings or runner identifiers.
    result = {"kernel": platform.release(), "architecture": platform.machine(), "os": platform.system()}
    cpu = json.loads(subprocess.check_output(["lscpu", "-J"], text=True))
    result["cpu"] = {item["field"]: item["data"] for item in cpu["lscpu"]
                     if item["field"] in {"Architecture:", "CPU(s):", "Vendor ID:", "Model name:", "Hypervisor vendor:"}}
    result["load_before_block"] = Path("/proc/loadavg").read_text().split()[:3]
    docker = ["docker", "info", "--format", "{{.ServerVersion}}|{{.CgroupVersion}}|{{.NCPU}}|{{.MemTotal}}|{{.Driver}}"]
    result["docker"] = subprocess.check_output(docker, text=True).strip()
    return result


def run_block(block, output):
    if platform.system() != "Linux" or platform.machine() != "x86_64" or os.geteuid() == 0:
        raise SystemExit("Actual non-root Linux amd64 is required.")
    controller = git("rev-parse", "HEAD")
    for revision in (BASE, HEAD, INSTRUMENT):
        assert git("rev-parse", revision) == revision
    assert blob(BASE, PROGRAM) == blob(HEAD, PROGRAM)
    assert blob(BASE, "tools/ci/native-runtime.sh") == blob(HEAD, "tools/ci/native-runtime.sh")
    assert blob(BASE, ".github/workflows/ci.yml") == blob(HEAD, ".github/workflows/ci.yml")
    output.mkdir(parents=True, exist_ok=False)
    context = {"controller_revision": controller, "instrumentation_revision": INSTRUMENT,
               "block": block, "order": ORDERS[block], "environment": environment(), "trials": []}
    (output / "manifest.json").write_text(json.dumps(context, indent=2))
    labels = set(re.findall(r'Check\("([^"]+)"\)', blob(BASE, PROGRAM).decode()))
    for order, condition in enumerate(ORDERS[block], 1):
        source = BASE if condition.startswith("base-") else HEAD
        instrumented = condition.endswith("-instrumented")
        trial_id = f"block-{block}-{order}-{condition}"
        tree = output.parent / ("source-" + trial_id)
        subprocess.run(["git", "worktree", "add", "--detach", str(tree), source], cwd=REPO, check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        # Same runner/evidence bookkeeping for both conditions. Only full observers differ.
        (tree / RUNNER).write_bytes((REPO / RUNNER).read_bytes())
        (tree / SAMPLER).write_bytes(blob(INSTRUMENT, SAMPLER))
        if instrumented: (tree / PROGRAM).write_bytes(blob(INSTRUMENT, PROGRAM))
        subprocess.run(["git", "diff", "--exit-code", source, "--", "src", "tests", "tools/cover-native"],
                       cwd=tree, check=True, stdout=subprocess.DEVNULL)
        hashes = {path: hashlib.sha256((tree / path).read_bytes()).hexdigest() for path in (PROGRAM, RUNNER, SAMPLER)}
        trial = {"id": trial_id, "source_revision": source, "instrumentation_revision": INSTRUMENT,
                 "controller_revision": controller, "instrumented": instrumented, "overlay_sha256": hashes,
                 "host_load_before": Path("/proc/loadavg").read_text().split()[:3],
                 "started_utc": datetime.datetime.now(datetime.timezone.utc).isoformat()}
        context["trials"].append(trial)
        (output / "manifest.json").write_text(json.dumps(context, indent=2))
        print("TRIAL_BEGIN " + trial_id, flush=True)
        start = time.monotonic_ns()
        env = {**os.environ, "NATIVE_AB_SAMPLE": "1" if instrumented else "0", "BUILDX_GIT_INFO": "false"}
        process = subprocess.Popen(["bash", "tools/ci/native-runtime.sh"], cwd=tree, env=env,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
        lines, dropped = [], 0
        with (output / (trial_id + ".log")).open("w") as log:
            for line in process.stdout:
                clean = sanitize(line, labels)
                if clean is None:
                    dropped += 1
                else:
                    lines.append(clean)
                    log.write(clean + "\n")
                    log.flush()
        exit_code = process.wait()
        trial.update({"duration_ns_including_build": time.monotonic_ns() - start,
                      "finished_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      "unretained_output_lines": dropped, "result": summarize(lines, exit_code)})
        (output / (trial_id + ".json")).write_text(json.dumps(trial, indent=2))
        (output / "manifest.json").write_text(json.dumps(context, indent=2))
        print("TRIAL_END " + trial_id + " " + json.dumps({key: trial["result"][key] for key in
              ("exit_code", "oom_killed", "last_pass", "cgroup_peak_observed", "complete_workload_pass")}), flush=True)
    return 0 if all(trial["result"]["complete_workload_pass"] for trial in context["trials"]) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--block", type=int, choices=ORDERS, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    raise SystemExit(run_block(args.block, args.output))
