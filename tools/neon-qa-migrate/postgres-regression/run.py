"""Run the actual catalog-query regression on a NEW private Unix-socket cluster."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import resource
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("operator_entry", HERE.parent / "run.py")
operator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(operator)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pg-bin", required=True, help="Existing native PostgreSQL 18.7 bin directory; never downloaded")
    parser.add_argument("--test-parent", help="External directory for a new disposable test tree")
    args = parser.parse_args()
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    binaries = Path(args.pg_bin).resolve()
    if not binaries.is_absolute() or not all((binaries / n).is_file() for n in ("postgres", "initdb", "pg_ctl")):
        raise ValueError("Existing PostgreSQL server binaries required")
    parent = Path(args.test_parent or tempfile.gettempdir()).resolve()
    if parent.is_relative_to(operator.ROOT):
        raise ValueError("Test data must be outside the repository")
    test = Path(tempfile.mkdtemp(prefix="p18-", dir=parent)).resolve()
    if len(os.fsencode(test / ".s.PGSQL.65439")) >= 104:
        raise ValueError("Use a shorter private test parent for the Unix socket")
    (test / "home").mkdir(mode=0o700)
    driver = Path.home() / ".nuget/packages/npgsql/10.0.3/lib/net10.0/Npgsql.dll"
    if hashlib.sha256(driver.read_bytes()).hexdigest() != operator.DRIVER_SHA256:
        raise ValueError("Pinned cached Npgsql driver required")
    env = {"PATH": f"{binaries}:/usr/local/share/dotnet:/usr/local/bin:/usr/bin:/bin",
           "HOME": str(test / "home"), "TMPDIR": str(test), "LC_ALL": "C",
           "DOTNET_CLI_HOME": str(test / "home"), "DOTNET_NOLOGO": "1",
           "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
           "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false", "MSBuildEnableWorkloadResolver": "false",
           "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true", "COMPlus_DbgEnableMiniDump": "0",
           "NUGET_PACKAGES": str(driver.parents[4])}

    def run(command, timeout=60):
        return operator.isolated_process(command, env, timeout, capture=True)

    version = run([str(binaries / "postgres"), "--version"], 10)
    if version.returncode or version.stdout.decode().strip() != "postgres (PostgreSQL) 18.7":
        raise ValueError("Full runner policy regression requires PostgreSQL 18.7")
    build = run(["dotnet", "build", str(HERE / "Regression.csproj"), "--configfile",
                 str(HERE.parent / "NuGet.Config"), "--disable-build-servers", "--nologo", "-v", "quiet",
                 f"-p:NpgsqlPath={driver}", "-p:NuGetAudit=false", "-p:UseSharedCompilation=false", "-m:1", "-nr:false"], 180)
    (test / "build.log").write_bytes(build.stdout + build.stderr)
    if build.returncode:
        print("Regression build failed; no server started. Public build log:", test / "build.log")
        return 1
    data = test / "data"
    init = run([str(binaries / "initdb"), "-D", str(data), "--username=regression_admin",
                "--auth-local=trust", "--auth-host=reject", "--no-locale", "--encoding=UTF8"])
    (test / "initdb.log").write_bytes(init.stdout + init.stderr)
    if init.returncode:
        raise ValueError("Isolated initdb failed; no server started")
    (data / "postgresql.auto.conf").write_text(
        f"listen_addresses = ''\nunix_socket_directories = '{str(test).replace(chr(39), chr(39)*2)}'\n"
        "port = 65439\nunix_socket_permissions = 0700\nmax_connections = 10\nshared_buffers = '16MB'\n")
    (test / "regression.marker").write_text("isolated-postgres-regression\n")
    (test / "regression.marker").chmod(0o600)
    try:
        start = run([str(binaries / "pg_ctl"), "-D", str(data), "-l", str(test / "server.log"), "-w", "-t", "30", "start"], 40)
        if start.returncode:
            raise ValueError("Private socket server startup failed")
        result = run(["dotnet", str(HERE / "bin/Debug/net10.0/Regression.dll"), str(test)], 60)
        (test / "regression.log").write_bytes(result.stdout + result.stderr)
        print(result.stdout.decode(), end="")
        return result.returncode
    finally:
        stopped = True
        if (data / "postmaster.pid").exists():
            stop = run([str(binaries / "pg_ctl"), "-D", str(data), "-m", "fast", "-w", "-t", "30", "stop"], 40)
            (test / "stop.log").write_bytes(stop.stdout + stop.stderr)
            stopped = stop.returncode == 0
        stopped = stopped and not (data / "postmaster.pid").exists() and not (test / ".s.PGSQL.65439").exists()
        evidence = {"isolatedClusterStopped": stopped, "tcpDisabled": True,
                    "remoteConnectionAttempted": False, "evidenceDirectory": str(test)}
        (test / "cleanup.json").write_text(json.dumps(evidence, indent=2) + "\n")
        print(json.dumps(evidence))
        if not stopped:
            raise ValueError("Isolated test shutdown was not confirmed")


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, subprocess.SubprocessError):
        print("Local regression setup/process failure; raw diagnostics withheld. No remote endpoint is supported.")
        sys.exit(1)
