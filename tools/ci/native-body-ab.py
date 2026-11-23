"""Frozen same-application harness x observer experiment; diagnostic branch only."""
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

spec = importlib.util.spec_from_file_location("native_ab", Path(__file__).with_name("native-ab.py"))
ab = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ab)
APP = "5483b200a8d960d9c2b99cf15e04448283059466"
INSTRUMENT = "a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597"
CANDIDATE = "1c74f42f02ed848b3072f69e462a61e85456fbfb"
BOOKKEEPING = "2a8b0841abee0f2c54ae9f6c775491048123270b"
ORDERS = {
    1: ["original-instrumented", "candidate-instrumented", "original-control", "candidate-control"],
    2: ["candidate-control", "original-control", "candidate-instrumented", "original-instrumented"],
    3: ["original-control", "candidate-control", "original-instrumented", "candidate-instrumented"],
}
OLD_ASSERTION = "Assert.Equal(2097152, (await response.Content.ReadAsByteArrayAsync(token)).Length);"
NEW_ASSERTION = "await BufferedResponseBody.AssertLengthAsync(response.Content, 2097152, token);"
HELPER_START = "// GetAsync has already buffered the body. Count through EOF without making another full copy.\n"
HELPER_END = "internal sealed class DatabaseLifetime : IAsyncDisposable"


def candidate_program(original):
    fixed = ab.blob(CANDIDATE, ab.PROGRAM).decode()
    helper = HELPER_START + fixed.split(HELPER_START, 1)[1].split(HELPER_END, 1)[0]
    assert original.count(OLD_ASSERTION) == 1 and original.count(HELPER_END) == 1
    assert HELPER_START not in original
    return original.replace(OLD_ASSERTION, NEW_ASSERTION).replace(HELPER_END, helper + HELPER_END)


def verify_sources():
    assert ab.git("rev-parse", APP) == APP
    assert ab.git("rev-parse", INSTRUMENT) == INSTRUMENT
    assert candidate_program(ab.blob(INSTRUMENT, ab.PROGRAM).decode()).encode() == ab.blob(CANDIDATE, ab.PROGRAM)
    # No application, fixture, existing test, required-check, native or resource changes.
    paths = ["src", "tests", ".github/workflows/ci.yml", "tools/cover-native", "tools/cover-api/StorageFixture.cs",
             "tools/cover-api/Dockerfile", "tools/ci/native-runtime.sh", "tools/ci/postgres-image.txt"]
    assert not ab.git("diff", "--name-only", APP, "HEAD", "--", *paths)
    assert ab.blob(BOOKKEEPING, ab.RUNNER) == (ab.REPO / ab.RUNNER).read_bytes()
    assert ab.blob(INSTRUMENT, ab.SAMPLER) == (ab.REPO / ab.SAMPLER).read_bytes()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2))


def capture(command, cwd, log_path, labels, env=None, regression=False):
    lines, dropped = [], 0
    names = set(re.findall(r'\("([a-zA-Z0-9-]+)",', (ab.REPO / "tools/cover-api-body-tests/Program.cs").read_text()))
    process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, errors="replace")
    with log_path.open("w") as log:
        for raw in process.stdout:
            line = raw.strip()
            fixed = (line.startswith("BODY_PASS ") and line.removeprefix("BODY_PASS ") in names
                     or re.fullmatch(r"BODY_TESTS passed=12 failed=0|BODY_ALLOCATION originalBytes=\d+ candidateBytes=\d+", line))
            clean = line if regression and fixed else ab.sanitize(raw, labels)
            if clean is None:
                dropped += 1
            else:
                lines.append(clean)
                log.write(clean + "\n")
                log.flush()
    return process.wait(), lines, dropped


def run_regressions(output):
    verify_sources()
    output.mkdir(parents=True, exist_ok=False)
    result = {"controller_revision": ab.git("rev-parse", "HEAD"), "candidate_revision": ab.git("rev-parse", CANDIDATE),
              "application_revision": APP, "commands": [], "complete_pass": False}
    write_json(output / "regressions.json", result)
    build = output.parent / "body-regression-build"
    commands = [
        ["bash", "tools/cover-native/prepare.sh", "linux-x64"],
        ["dotnet", "build", "tools/cover-api-body-tests/BodyTests.csproj", "-c", "Release", "--nologo",
         "--artifacts-path", str(build), "-p:NuGetAudit=false"],
        ["bash", "tools/ci/run-isolated.sh", "none", "dotnet",
         str(build / "bin/BodyTests/release/CoverApiVerification.BodyTests.dll")],
    ]
    for index, command in enumerate(commands, 1):
        code, lines, dropped = capture(command, ab.REPO, output / f"step-{index}.log", set(), regression=True)
        result["commands"].append({"step": index, "exit_code": code, "unretained_output_lines": dropped})
        if index == 3:
            result["complete_pass"] = (code == 0 and "BODY_TESTS passed=12 failed=0" in lines
                                       and sum(line.startswith("BODY_PASS ") for line in lines) == 12)
            for line in lines: print(line, flush=True)
        write_json(output / "regressions.json", result)
        if code != 0: return code
    return 0 if result["complete_pass"] else 1


def run_block(block, output):
    if platform.system() != "Linux" or platform.machine() != "x86_64" or os.geteuid() == 0:
        raise SystemExit("Actual non-root Linux amd64 is required.")
    verify_sources()
    output.mkdir(parents=True, exist_ok=False)
    controller = ab.git("rev-parse", "HEAD")
    candidate = ab.git("rev-parse", CANDIDATE)
    context = {"controller_revision": controller, "application_revision": APP,
               "candidate_revision": candidate, "instrumentation_revision": INSTRUMENT,
               "bookkeeping_revision": BOOKKEEPING, "block": block, "order": ORDERS[block],
               "environment": ab.environment(), "trials": []}
    write_json(output / "manifest.json", context)
    labels = set(re.findall(r'Check\("([^"]+)"\)', ab.blob(APP, ab.PROGRAM).decode()))
    for order, condition in enumerate(ORDERS[block], 1):
        instrumented = condition.endswith("-instrumented")
        changed = condition.startswith("candidate-")
        trial_id = f"block-{block}-{order}-{condition}"
        tree = output.parent / ("source-" + trial_id)
        subprocess.run(["git", "worktree", "add", "--detach", str(tree), APP], cwd=ab.REPO, check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        program = ab.blob(INSTRUMENT if instrumented else APP, ab.PROGRAM).decode()
        if changed: program = candidate_program(program)
        (tree / ab.PROGRAM).write_text(program)
        (tree / ab.RUNNER).write_bytes(ab.blob(BOOKKEEPING, ab.RUNNER))
        (tree / ab.SAMPLER).write_bytes(ab.blob(INSTRUMENT, ab.SAMPLER))
        # Test-only friend attribute/standalone runner do not enter either measured image.
        subprocess.run(["git", "diff", "--exit-code", APP, "--", "src", "tests", "tools/cover-native",
                        "tools/cover-api/Verify.csproj", "tools/cover-api/StorageFixture.cs", "tools/cover-api/Dockerfile",
                        ".github/workflows/ci.yml", "tools/ci/native-runtime.sh"],
                       cwd=tree, check=True, stdout=subprocess.DEVNULL)
        trial = {"id": trial_id, "source_revision": APP, "application_revision": APP,
                 "harness_variant": "candidate" if changed else "original", "candidate_revision": candidate,
                 "instrumentation_revision": INSTRUMENT, "controller_revision": controller, "instrumented": instrumented,
                 "overlay_sha256": {path: hashlib.sha256((tree / path).read_bytes()).hexdigest()
                                    for path in (ab.PROGRAM, ab.RUNNER, ab.SAMPLER)},
                 "started_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                 "host_load_before": Path("/proc/loadavg").read_text().split()[:3]}
        context["trials"].append(trial)
        write_json(output / "manifest.json", context)
        print("TRIAL_BEGIN " + trial_id, flush=True)
        env = {**os.environ, "NATIVE_AB_SAMPLE": "1" if instrumented else "0", "BUILDX_GIT_INFO": "false"}
        started = time.monotonic_ns()
        code, lines, dropped = capture(["bash", "tools/ci/native-runtime.sh"], tree,
                                       output / (trial_id + ".log"), labels, env=env)
        result = ab.summarize(lines, code)
        final = next((point for point in result["gc_snapshots"] if point["phase"] == "final"), None)
        result["total_managed_allocated_bytes"] = int(final["allocatedBytes"]) if final else None
        trial.update({"finished_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      "duration_ns_including_build": time.monotonic_ns() - started,
                      "unretained_output_lines": dropped, "result": result})
        write_json(output / (trial_id + ".json"), trial)
        write_json(output / "manifest.json", context)
        print("TRIAL_END " + trial_id + " " + json.dumps({key: result[key] for key in
              ("exit_code", "oom_killed", "last_pass", "total_managed_allocated_bytes", "cgroup_peak_observed", "complete_workload_pass")}), flush=True)
    return 0 if all(trial["result"]["complete_workload_pass"] for trial in context["trials"]) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--block", type=int, choices=ORDERS)
    mode.add_argument("--regressions", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    raise SystemExit(run_regressions(args.output) if args.regressions else run_block(args.block, args.output))
