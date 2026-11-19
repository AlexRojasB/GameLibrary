"""Fail closed before executing a test command in the Linux CI namespace."""

import os
import socket
import sys
from pathlib import Path


def main():
    if len(sys.argv) < 2:
        raise SystemExit("A test command is required.")
    if sys.platform != "linux" or os.geteuid() == 0 or os.getgroups():
        raise SystemExit("Expected unprivileged Linux process without supplementary groups.")
    status = dict(line.split(":", 1) for line in Path("/proc/self/status").read_text().splitlines() if ":" in line)
    capability_sets = ("CapEff", "CapPrm", "CapBnd", "CapAmb")
    if status["NoNewPrivs"].strip() != "1" or any(int(status[key].strip(), 16) for key in capability_sets):
        raise SystemExit("Privilege escalation/capabilities are not disabled.")
    # Query the current network namespace, not the host's existing sysfs mount.
    if {name for _, name in socket.if_nameindex()} != {"lo"}:
        raise SystemExit("Only loopback may be available to tests.")
    for docker_socket in ("/var/run/docker.sock", "/run/docker.sock", Path.home() / ".docker/run/docker.sock"):
        if os.access(docker_socket, os.R_OK | os.W_OK):
            raise SystemExit("Test processes must not have Docker socket access.")
    # Documentation-only addresses: never probe a real provider or QA endpoint.
    for family, address in [(socket.AF_INET, "192.0.2.1"), (socket.AF_INET6, "2001:db8::1")]:
        with socket.socket(family, socket.SOCK_STREAM) as probe:
            probe.settimeout(0.25)
            try:
                probe.connect((address, 443))
            except OSError:
                continue
            raise SystemExit("Unexpected external connectivity.")
    print("CI isolation: loopback only, no capabilities, no-new-privileges; executing test command.", flush=True)
    os.execvp(sys.argv[1], sys.argv[1:])


if __name__ == "__main__":
    main()
