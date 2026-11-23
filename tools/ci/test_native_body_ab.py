"""Portable protocol/source checks. Compiled body behavior runs separately."""
import importlib.util
import re
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("native_body_ab", Path(__file__).with_name("native-body-ab.py"))
body = importlib.util.module_from_spec(spec)
spec.loader.exec_module(body)


class NativeBodyAbTests(unittest.TestCase):
    def test_exact_committed_candidate_is_the_minimal_instrumented_overlay(self):
        original = body.ab.blob(body.INSTRUMENT, body.ab.PROGRAM).decode()
        self.assertEqual(body.candidate_program(original).encode(), body.ab.blob(body.CANDIDATE, body.ab.PROGRAM))

    def test_control_changes_only_the_same_assertion_and_helper(self):
        original = body.ab.blob(body.APP, body.ab.PROGRAM).decode()
        changed = body.candidate_program(original)
        helper = body.HELPER_START + changed.split(body.HELPER_START, 1)[1].split(body.HELPER_END, 1)[0]
        self.assertEqual(changed.replace(helper, "").replace(body.NEW_ASSERTION, body.OLD_ASSERTION), original)

    def test_request_privacy_concurrency_fixtures_and_order_are_identical(self):
        original = body.ab.blob(body.INSTRUMENT, body.ab.PROGRAM).decode()
        changed = body.candidate_program(original)
        # Everything in Gate except the single length assertion must remain byte-identical.
        before = original.split("internal sealed class DatabaseLifetime", 1)[0]
        after = changed.split(body.HELPER_START, 1)[0]
        self.assertEqual(after.replace(body.NEW_ASSERTION, body.OLD_ASSERTION), before)
        self.assertIn("Assert.Equal(8, provider.ActiveReads);", after)
        self.assertIn("Assert.Equal(2, provider.ActiveWrites);", after)
        self.assertEqual(len(re.findall(r'Check\("', before)), len(re.findall(r'Check\("', after)))

    def test_all_three_blocks_are_paired_and_alternate_variants(self):
        expected = {"original-control", "candidate-control", "original-instrumented", "candidate-instrumented"}
        self.assertEqual(len(body.ORDERS), 3)
        for order in body.ORDERS.values():
            self.assertEqual(set(order), expected)
            self.assertEqual(len(order), 4)
            for left, right in zip(order, order[1:]):
                self.assertNotEqual(left.split("-")[0], right.split("-")[0])
            for left, right in (order[:2], order[2:]):
                self.assertEqual(left.split("-")[1], right.split("-")[1])

    def test_helper_has_bounded_reads_and_asserts_only_after_EOF(self):
        fixed = body.ab.blob(body.CANDIDATE, body.ab.PROGRAM).decode()
        helper = fixed.split(body.HELPER_START, 1)[1].split(body.HELPER_END, 1)[0]
        self.assertIn("new byte[16 * 1024]", helper)
        self.assertIn("await using var stream", helper)
        self.assertIn("while ((read = await stream.ReadAsync(buffer, ct)) != 0)", helper)
        self.assertIn("Assert.Equal(expected, length);", helper)
        self.assertNotIn("ReadAsByteArray", helper)
        self.assertNotIn("catch", helper)
        self.assertNotIn("GC.", helper)


if __name__ == "__main__":
    unittest.main()
