"""Isolated operator entry point. No network, credentials or private inputs by default."""

import argparse
import hashlib
import os
from pathlib import Path
import resource
import signal
import stat
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DRIVER_SHA256 = "e2bcafe1660fe4543fe9dfcabed972ec8d32259e5ea8009462ce1de23c7034c2"


def isolated_process(command, env, timeout, capture=False):
    # Own process group: on timeout stop only this invocation's build/compiler or
    # runner children, not user processes or unrelated dotnet services.
    with subprocess.Popen(command, cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
                          stdout=subprocess.PIPE if capture else None,
                          stderr=subprocess.PIPE if capture else None,
                          start_new_session=True) as process:
        try:
            stdout, stderr = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.communicate()
            raise
        return subprocess.CompletedProcess(command, process.returncode, stdout, stderr)


def private_path(value):
    p = Path(value)
    if not p.is_absolute() or p.resolve() != p or p.is_relative_to(ROOT):
        raise ValueError("Private input must be canonical and outside the repository")
    s, parent = p.lstat(), p.parent.stat()
    if (not stat.S_ISREG(s.st_mode) or s.st_uid != os.getuid()
            or stat.S_IMODE(s.st_mode) != 0o600 or s.st_size not in range(1, 65537)
            or parent.st_uid != os.getuid() or stat.S_IMODE(parent.st_mode) != 0o700):
        raise ValueError("Private input requires owner-only 0600 file in owner-only 0700 directory")
    return str(p)


def main():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--offline", action="store_true")
    modes.add_argument("--self-test", action="store_true")
    modes.add_argument("--preflight", action="store_true")
    modes.add_argument("--execute", action="store_true")
    parser.add_argument("--sql", required=True)
    parser.add_argument("--authorization-file")
    parser.add_argument("--metadata-file")
    parser.add_argument("--passfile")
    parser.add_argument("--ca-file")
    args = parser.parse_args()
    mode = "execute" if args.execute else "preflight" if args.preflight else "self-test" if args.self_test else "offline"
    inputs = [args.authorization_file, args.metadata_file, args.passfile, args.ca_file]
    if mode in ("offline", "self-test") and any(inputs):
        parser.error("Offline modes reject private/network inputs")
    if mode in ("execute", "preflight") and not all(inputs):
        parser.error("Network modes require separate future authorization and all explicit inputs")
    # Validate metadata only here, never read or copy the credential file.
    if mode in ("execute", "preflight"):
        for name in ("authorization_file", "metadata_file", "passfile"):
            setattr(args, name, private_path(getattr(args, name)))
        ca = Path(args.ca_file)
        if not ca.is_absolute() or not ca.is_file():
            raise ValueError("Explicit public CA trust bundle required")
    driver = Path.home() / ".nuget/packages/npgsql/10.0.3/lib/net10.0/Npgsql.dll"
    if hashlib.sha256(driver.read_bytes()).hexdigest() != DRIVER_SHA256:
        raise ValueError("Cached Npgsql 10.0.3 binary missing or changed; no download/fallback")
    # Deliberately do not inherit PG*, connection strings, user-secrets, DOTNET startup
    # hooks/profilers, proxy variables or alternate NuGet sources. No shell expansion.
    with tempfile.TemporaryDirectory(prefix="game-library-neon-runner-") as home:
        env = {"PATH": "/usr/local/share/dotnet:/usr/local/bin:/usr/bin:/bin",
               "HOME": home, "DOTNET_CLI_HOME": home, "DOTNET_NOLOGO": "1",
               "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
               "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true", "COMPlus_DbgEnableMiniDump": "0",
               "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
               "MSBuildEnableWorkloadResolver": "false", "NUGET_PACKAGES": str(driver.parents[4])}
        build = isolated_process(["dotnet", "build", str(HERE / "Runner.csproj"), "--configfile",
                                str(HERE / "NuGet.Config"), "--disable-build-servers", "--nologo",
                                "-v", "quiet", f"-p:NpgsqlPath={driver}", "-p:NuGetAudit=false",
                                "-p:UseSharedCompilation=false", "-m:1", "-nr:false"],
                               env, timeout=180, capture=True)
        if build.returncode:
            print("Runner build failed; no private inputs consumed or database connection attempted.")
            # Build only handles public source and the cached driver; safe diagnostics.
            print(build.stdout.decode(errors="replace"))
            return 1
        command = ["dotnet", str(HERE / "bin/Debug/net10.0/Runner.dll"), mode, str(ROOT),
                   str(Path(args.sql).resolve())]
        if mode in ("execute", "preflight"):
            command += [args.authorization_file, args.metadata_file, args.passfile, args.ca_file]
        # Child has sanitized structured output only. Never print raw exceptions.
        result = isolated_process(command, env, timeout=660)
        return result.returncode


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, subprocess.SubprocessError):
        print("Local runner input/build/process failure; raw diagnostics withheld. Do not automatically retry execution.")
        sys.exit(1)
