"""Host-side, numeric-only cgroup-v2 sampling for one disposable native harness.

Runs outside the measured container. Never reads argv, environ, maps, request data
or Docker configuration. No network/subprocess calls and no privileges inside tests.
"""

import argparse
import datetime
import json
import os
import resource
import time
from pathlib import Path

PROC = Path("/proc")
CGROUP = Path("/sys/fs/cgroup")
EVENTS = {"low", "high", "max", "oom", "oom_kill", "oom_group_kill"}
MEMORY = {"anon", "file", "kernel", "kernel_stack", "pagetables", "sock", "shmem", "slab", "file_mapped"}
STATUS = {"Pid", "PPid", "VmRSS", "VmHWM", "VmSize", "VmData", "VmSwap", "RssAnon", "RssFile", "RssShmem", "Threads"}
SMAPS = {"Rss", "Pss", "Pss_Anon", "Pss_File", "Pss_Shmem", "Private_Clean", "Private_Dirty", "Shared_Clean", "Shared_Dirty", "Anonymous", "Swap"}


def read(path):
    try:
        with path.open() as stream:
            value = stream.read(65537)
        return value if len(value) <= 65536 else None
    except (OSError, ValueError):
        return None


def number(value):
    return int(value) if value is not None and len(value) <= 20 and value.isascii() and value.isdecimal() else None


def counters(path, allowed):
    text = read(path)
    if text is None:
        return None
    result = {}
    for line in text.splitlines():
        parts = line.replace(":", " ").split()
        if len(parts) in (2, 3) and parts[0] in allowed:
            value = number(parts[1])
            if value is not None and (len(parts) == 2 or parts[2] == "kB"):
                result[parts[0]] = value * (1024 if len(parts) == 3 else 1)
    return result


def locate(pid):
    text = read(PROC / str(pid) / "cgroup")
    if text is None:
        return None
    paths = [line[3:] for line in text.splitlines() if line.startswith("0::/")]
    if len(paths) != 1 or paths[0] == "/":
        return None  # Never sample all host processes/the root cgroup.
    base = CGROUP.resolve()
    candidate = (base / paths[0].lstrip("/")).resolve()
    return candidate if candidate.is_relative_to(base) and candidate != base else None


def sample(group):
    raw_pids = read(group / "cgroup.procs")
    pids = sorted({value for item in (raw_pids or "").split()
                   if (value := number(item)) is not None and value > 0})
    # The unchanged container pids-limit is 256. Bound output on a bad fixture/path.
    processes = []
    for pid in pids[:256]:
        # Only current members of this exact cgroup; no host-wide process scan.
        if locate(pid) != group:
            continue
        processes.append({"pid": pid, "status": counters(PROC / str(pid) / "status", STATUS),
                          "smaps_rollup": counters(PROC / str(pid) / "smaps_rollup", SMAPS)})
    scalars = {}
    for name in ("memory.current", "memory.peak", "memory.max"):
        text = read(group / name)
        scalars[name] = number(text.strip()) if text is not None else None
    return {"utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "monotonic_ns": time.monotonic_ns(), "memory": scalars,
            "events": counters(group / "memory.events", EVENTS),
            "stat": counters(group / "memory.stat", MEMORY),
            "member_count": len(pids) if raw_pids is not None else None,
            "truncated": len(pids) > 256, "processes": processes}


def emit(value):
    print("NATIVE_EXTERNAL " + json.dumps(value, separators=(",", ":")), flush=True)


def monitor(pid, stop_file, interval=1.0):
    group = locate(pid)
    if group is None:
        emit({"state": "unavailable", "reason": "target-cgroup-not-found"})
        return
    started = time.monotonic_ns()
    cpu = time.process_time_ns()
    count = 0
    while True:
        value = sample(group)
        stopped = stop_file.exists()
        empty = value["member_count"] == 0
        missing = value["memory"]["memory.current"] is None
        reason = "stop-requested" if stopped else "target-empty" if empty else "cgroup-unavailable" if missing else None
        value.update({"sample": count + 1, "final": stopped or empty or missing, "stop_reason": reason})
        emit(value)
        count += 1
        if stopped or empty or missing:
            break
        time.sleep(interval)
    emit({"state": "sampler-finished", "samples": count,
          "elapsed_ns": time.monotonic_ns() - started, "cpu_ns": time.process_time_ns() - cpu,
          "sampler_peak_rss_kib": resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,
          "interval_seconds": interval})


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--stop-file", type=Path, required=True)
    args = parser.parse_args()
    if os.name != "posix" or not PROC.exists() or args.pid <= 0:
        raise SystemExit("A live Linux container PID is required.")
    monitor(args.pid, args.stop_file)
