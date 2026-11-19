"""Unit tests of fail-closed decisions; mocks do NOT prove Linux kernel isolation."""

import importlib.util
import io
import socket
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("isolation", Path(__file__).with_name("assert-isolated.py"))
isolation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(isolation)

SAFE_STATUS = "NoNewPrivs:\t1\nCapBnd:\t0000\nCapEff:\t0000\nCapPrm:\t0000\nCapAmb:\t0000\n"


class IsolationGuardTests(unittest.TestCase):
    def setUp(self):
        self.argv = self.mock("sys.argv", ["assert-isolated.py", "test-command", "argument"])
        self.platform = self.mock("sys.platform", "linux")
        self.uid = self.mock("os.geteuid", return_value=1000)
        self.groups = self.mock("os.getgroups", return_value=[])
        self.status = self.mock("pathlib.Path.read_text", return_value=SAFE_STATUS)
        self.interfaces = self.mock("socket.if_nameindex", return_value=[(1, "lo")])
        self.access = self.mock("os.access", return_value=False)
        self.sockets = self.mock("socket.socket")
        self.probe = self.sockets.return_value.__enter__.return_value
        self.probe.connect.side_effect = OSError("Network unreachable")
        self.execute = self.mock("os.execvp")
        self.mock("sys.stdout", io.StringIO())

    def mock(self, target, *args, **kwargs):
        mocker = patch(target, *args, **kwargs)
        self.addCleanup(mocker.stop)
        return mocker.start()

    def assert_rejected(self):
        with self.assertRaises(SystemExit):
            isolation.main()
        self.execute.assert_not_called()

    def test_missing_command_is_rejected(self):
        self.argv[:] = ["assert-isolated.py"]
        self.assert_rejected()

    def test_non_linux_is_rejected(self):
        isolation.sys.platform = "darwin"
        self.assert_rejected()

    def test_root_is_rejected(self):
        self.uid.return_value = 0
        self.assert_rejected()

    def test_supplementary_groups_are_rejected(self):
        self.groups.return_value = [999]
        self.assert_rejected()

    def test_privilege_escalation_is_rejected(self):
        self.status.return_value = SAFE_STATUS.replace("NoNewPrivs:\t1", "NoNewPrivs:\t0")
        self.assert_rejected()

    def test_remaining_capabilities_are_rejected(self):
        for key in ("CapBnd", "CapEff", "CapPrm", "CapAmb"):
            with self.subTest(capability_set=key):
                self.status.return_value = SAFE_STATUS.replace(key + ":\t0000", key + ":\t0001")
                self.assert_rejected()

    def test_connected_interface_is_rejected(self):
        self.interfaces.return_value = [(1, "lo"), (2, "eth0")]
        self.assert_rejected()

    def test_docker_socket_access_is_rejected(self):
        self.access.return_value = True
        self.assert_rejected()

    def test_ipv4_connectivity_is_rejected(self):
        self.probe.connect.side_effect = None
        self.assert_rejected()

    def test_ipv6_connectivity_is_rejected(self):
        self.probe.connect.side_effect = [OSError("Network unreachable"), None]
        self.assert_rejected()

    def test_only_documentation_addresses_are_probed_before_exec(self):
        isolation.main()
        self.assertEqual(self.sockets.call_args_list[0].args, (socket.AF_INET, socket.SOCK_STREAM))
        self.assertEqual(self.sockets.call_args_list[1].args, (socket.AF_INET6, socket.SOCK_STREAM))
        self.assertEqual([call.args for call in self.probe.connect.call_args_list],
                         [(("192.0.2.1", 443),), (("2001:db8::1", 443),)])
        self.execute.assert_called_once_with("test-command", ["test-command", "argument"])


if __name__ == "__main__":
    unittest.main()
