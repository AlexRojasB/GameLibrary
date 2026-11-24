"""Fixed required-CI evidence collector. No diagnostic profiles or arbitrary commands.

Only fixed labels/numeric data reach console/artifacts. Unknown output is counted,
not stored. Missing results, invalid metrics, process/collector failures stay red.
"""

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MEMORY = 805306368
THRESHOLD = 671088640
RESOURCE_KEYS = set("memoryMax swapMax swapCurrent peak max high oom oomKill oomGroupKill swapHigh swapEventsMax swapFail threshold".split())
DOCKER_PATTERN = (r"NATIVE_CONTAINER exit=([0-9]+) oomKilled=(true|false) memory=([0-9]+) "
                  r"memorySwap=([0-9]+) nanoCpus=([0-9]+) pidsLimit=([0-9]+)")
NEGATIVE = "PASS missing/tampered native asset fails unavailable without decoder fallback."


def pass_manifest():
    labels = re.findall(r'Check\("([^"\n]+)"\)', (ROOT / "tools/cover-api/Program.cs").read_text())
    # Overlap runs for three formats; read/write timeout runs twice. No cases split.
    if len(labels) != 15:
        raise ValueError("Workload manifest changed; review required.")
    return [labels[0], *([labels[1]] * 3), *labels[2:4], *([labels[4]] * 2), *labels[5:]]


def fields(line):
    result = {}
    for part in line.split()[1:]:
        key, sep, value = part.partition("=")
        if not sep or key in result:
            raise ValueError("Invalid metric fields.")
        result[key] = value
    return result


def resource(line):
    value = fields(line)
    stage = value.pop("stage", None)
    if stage not in ("startup", "teardown-complete") or set(value) != RESOURCE_KEYS:
        raise ValueError("Incomplete resource record.")
    if not all(re.fullmatch(r"[0-9]{1,19}", n) and int(n) <= 9223372036854775807 for n in value.values()):
        raise ValueError("Invalid metric number.")
    return {"stage": stage, **{k: int(n) for k, n in value.items()}}


def safe_line(line, labels):
    line = line.strip()
    if len(line) > 65536:
        return None
    if line.startswith("NATIVE_RESOURCE "):
        try:
            resource(line)
            return line
        except ValueError:
            return "NATIVE_RESOURCE_ERROR code=invalid-metrics"
    if line == NEGATIVE:
        return line
    if match := re.fullmatch(r"PASS ([0-9]+): (.+)", line):
        return line if match[2] in labels else None
    patterns = [
        DOCKER_PATTERN,
        r"IMAGE TESTS passed=[0-9]+ failed=[0-9]+",
        r"RESOURCE pipelines=[0-9]+ operations=[0-9]+ JPEG=[0-9]+ progressive=[0-9]+ PNG=[0-9]+ elapsedMs=[0-9]+ processPeakBytes=[0-9]+",
        r"RESULT cases=[0-9]+ elapsedMs=[0-9]+ processCpuMs=[0-9]+ providerWrites=[0-9]+ providerReads=[0-9]+ providerDeletes=[0-9]+ providerCancellations=[0-9]+",
        r"TARGET runtime=\.NET [0-9.]+ os=Ubuntu [0-9.]+ LTS arch=X64 user=app",
        r"FIXTURES baseline=[0-9]+ progressive=[0-9]+ png=[0-9]+ JPEGpixels=[0-9]+ proxyBytes=[0-9]+ uploadBytes=[0-9]+",
        r"TIMEOUT operation=(?:read|write) elapsedMs=[0-9]+",
        r"RATE (?:batch=[0-9]+ accepted=[0-9]+ minuteRetrySeconds=[0-9.]+|hourlyAccepted=[0-9]+ hourRetrySeconds=[0-9.]+)",
        r"RECEIVE pulses=(?:True|False) elapsedMs=[0-9]+ status=408",
        r"SHUTDOWN activeProviderRead elapsedMs=[0-9]+",
        r"KESTREL minimumBodyBytesPerSecond=[0-9.]+ graceSeconds=[0-9.]+ shutdownSeconds=[0-9.]+",
        r"METRICS phase=(?:final|overlap-two-uploads-eight-2MiB-reads) workingBytes=[0-9]+ processPeakBytes=[0-9]+",
        r"CGROUP (?:final|overlap-two-uploads-eight-2MiB-reads) memory\.(?:current|peak|max)=[0-9]+",
        r"NATIVE_RESOURCE_ERROR code=(?:verification-failed|invalid-metrics|docker-inspection-failed)",
    ]
    if any(re.fullmatch(pattern, line) for pattern in patterns):
        return line
    return None


def valid_resource(value, memory):
    return (value["memoryMax"] == memory and value["threshold"] == THRESHOLD
            and 0 < value["peak"] <= min(THRESHOLD, memory)
            and all(value[key] == 0 for key in RESOURCE_KEYS - {"memoryMax", "peak", "threshold"}))


def assess(lines, exit_code, memory=MEMORY):
    expected = pass_manifest()
    errors = []
    passes = [line for line in lines if re.fullmatch(r"PASS [0-9]+: .+", line)]
    if passes != [f"PASS {i}: {label}" for i, label in enumerate(expected, 1)]:
        errors.append("incomplete-api-cases")
    images = [line for line in lines if line.startswith("IMAGE TESTS ")]
    operations = [fields(line) for line in lines if line.startswith("RESOURCE ")]
    if images != ["IMAGE TESTS passed=94 failed=0"]:
        errors.append("incomplete-images")
    if len(operations) != 1 or operations[0].get("pipelines") != "2" or operations[0].get("operations") != "60":
        errors.append("incomplete-operations")
    if lines.count(NEGATIVE) != 2:
        errors.append("incomplete-negative-controls")
    results = [fields(line) for line in lines if line.startswith("RESULT ")]
    if len(results) != 1 or any(results[0].get(k) != v for k, v in {
            "cases": "18", "providerWrites": "29", "providerReads": "31",
            "providerDeletes": "2", "providerCancellations": "5"}.items()):
        errors.append("incomplete-api-result")
    snapshots = []
    try:
        snapshots = [resource(line) for line in lines if line.startswith("NATIVE_RESOURCE ")]
        if ([s["stage"] for s in snapshots] != ["startup", "teardown-complete"]
                or not all(valid_resource(s, memory) for s in snapshots)
                or snapshots[-1]["peak"] < snapshots[0]["peak"]):
            errors.append("invalid-final-resources")
    except ValueError:
        errors.append("invalid-final-resources")
    containers = [re.fullmatch(DOCKER_PATTERN, line) for line in lines if line.startswith("NATIVE_CONTAINER ")]
    container = None
    if len(containers) != 1 or containers[0] is None:
        errors.append("missing-container-result")
    else:
        match = containers[0]
        container = dict(zip(("exit", "oomKilled", "memory", "memorySwap", "nanoCpus", "pidsLimit"), match.groups()))
        if match.groups() != ("0", "false", str(memory), str(memory), "2000000000", "256"):
            errors.append("container-exit-oom-or-limits")
    if exit_code != 0:
        errors.append("nonzero-exit")
    if any(line.startswith("NATIVE_RESOURCE_ERROR ") for line in lines):
        errors.append("resource-error")
    # A final record before workload completion or a late extra PASS is not completion.
    if not errors:
        startup = next(i for i, s in enumerate(lines) if s.startswith("NATIVE_RESOURCE stage=startup "))
        final = next(i for i, s in enumerate(lines) if s.startswith("NATIVE_RESOURCE stage=teardown-complete "))
        result = next(i for i, s in enumerate(lines) if s.startswith("RESULT "))
        first = lines.index(passes[0])
        last = lines.index(passes[-1])
        host = next(i for i, s in enumerate(lines) if s.startswith("NATIVE_CONTAINER "))
        if not startup < first <= last < result < final < host:
            errors.append("incomplete-measurement-window")
    coverage_errors = {"incomplete-api-cases", "incomplete-images", "incomplete-operations",
                       "incomplete-negative-controls", "incomplete-api-result"}
    return {"accepted": not errors, "coverage_complete": not coverage_errors.intersection(errors),
            "exit_code": exit_code, "errors": errors,
            "resources": snapshots, "container": container, "api_passes": len(passes)}


def inspect_container(container):
    template = ("NATIVE_CONTAINER exit={{.State.ExitCode}} oomKilled={{.State.OOMKilled}} "
                "memory={{.HostConfig.Memory}} memorySwap={{.HostConfig.MemorySwap}} "
                "nanoCpus={{.HostConfig.NanoCpus}} pidsLimit={{.HostConfig.PidsLimit}}")
    try:
        line = subprocess.check_output(["docker", "inspect", "--format", template, container],
                                       text=True, stderr=subprocess.DEVNULL).strip()
        match = re.fullmatch(DOCKER_PATTERN, line)
        if match is None:
            raise ValueError("Invalid Docker result.")
        print(line, flush=True)
        return 0 if match.groups() == ("0", "false", str(MEMORY), str(MEMORY), "2000000000", "256") else 1
    except (OSError, ValueError, subprocess.CalledProcessError):
        print("NATIVE_RESOURCE_ERROR code=docker-inspection-failed", flush=True)
        return 1


def collect(output, command=None, safe_extra=None):
    # command is internal for frozen diagnostic controller/tests; CLI always uses fixed gate.
    output.mkdir(parents=True, exist_ok=False)
    labels = pass_manifest()
    lines, dropped = [], 0
    result = {"accepted": False, "errors": ["collector-incomplete"]}
    process = None
    (output / "result.json").write_text(json.dumps(result))
    try:
        process = subprocess.Popen(command or ["bash", "tools/ci/native-runtime.sh"], cwd=ROOT,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                   errors="replace")
        with (output / "evidence.log").open("w") as log:
            while chunk := process.stdout.readline(65537):
                if len(chunk) > 65536 or not chunk.endswith("\n"):
                    while chunk and not chunk.endswith("\n"):
                        chunk = process.stdout.readline(65537)
                    dropped += 1
                    continue
                line = safe_line(chunk, labels)
                if line is None and safe_extra is not None:
                    line = safe_extra(chunk)
                if line is None:
                    dropped += 1
                elif len(lines) >= 2048:
                    process.terminate()
                    raise ValueError("Evidence overflow.")
                else:
                    lines.append(line)
                    log.write(line + "\n")
                    log.flush()
                    print(line, flush=True)
        status = process.wait()
        result = assess(lines, status)
    except (OSError, ValueError, subprocess.SubprocessError):
        # Fixed error only; never persist exception text or arbitrary command output.
        result = {"accepted": False, "errors": ["collector-failed"]}
    finally:
        if process is not None:
            if process.stdout is not None:
                process.stdout.close()
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
    result["unretained_output_lines"] = dropped
    result["evidence_sha256"] = hashlib.sha256((output / "evidence.log").read_bytes()).hexdigest() if (output / "evidence.log").exists() else None
    (output / "result.json").write_text(json.dumps(result, indent=2))
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--output", type=Path)
    group.add_argument("--inspect-container")
    args = parser.parse_args()
    raise SystemExit(inspect_container(args.inspect_container) if args.inspect_container else collect(args.output))
