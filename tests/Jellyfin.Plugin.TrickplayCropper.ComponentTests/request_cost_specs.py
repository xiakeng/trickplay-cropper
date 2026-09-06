"""Deterministic report contracts; no operator credentials or running host."""
import importlib.util
import pathlib
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "request_costs", ROOT / "tools/TrickplayCropper.IntegrationHarness/request_costs.py")
costs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(costs)


class RequestCostSpecs(unittest.TestCase):
    def test_retains_statistics_and_failed_request_volume(self):
        samples = [dict(phase="warm", method="HEAD", status=200, cache="", milliseconds=x)
                   for x in [1, 2, 6, 11]]
        samples.append(dict(phase="warm", method="HEAD", status=401, cache="", milliseconds=20))
        report = costs.render({"label": "test", "elapsed_seconds": 2}, samples)
        self.assertIn("| warm HEAD 200 | 4 | 1.000 | 11.000 | 4.000 | 5.000 |", report)
        self.assertIn("| warm HEAD 401 | 1 |", report)
        self.assertIn("Total requests: **5**", report)
        self.assertIn("Elapsed: **2.000 s**", report)

    def test_separates_status_disposition_and_load_shape(self):
        samples = [dict(phase=phase, method="GET", status=status, cache=cache, milliseconds=1)
                   for phase, status, cache in [("serial", 200, "HIT"), ("six-lane", 200, "MISS"),
                                                ("negative-source", 404, ""), ("conditional", 304, "HIT")]]
        report = costs.render({"label": "test", "elapsed_seconds": 1}, samples)
        for label in ["serial GET 200 HIT", "six-lane GET 200 MISS", "negative-source GET 404", "conditional GET 304 HIT"]:
            self.assertIn(label, report)
        self.assertNotIn("negative-source GET 404 HIT", report)

    def test_failed_response_is_retained_before_assertion(self):
        class RejectedClient:
            def request(self, *args):
                return 403, {}, b"", 7

        samples = []
        with self.assertRaises(ValueError):
            costs.measure(RejectedClient(), "warm", "GET", "/private-subject", samples, 200)
        self.assertEqual([dict(phase="warm", method="GET", status=403, cache="", milliseconds=7, server_timing="")], samples)
        self.assertNotIn("private-subject", str(samples))

    def test_success_must_expose_its_current_frame_index(self):
        class MissingIndexClient:
            def request(self, *args):
                return 304, {"etag": '"representation"'}, b"", 2

        with self.assertRaisesRegex(ValueError, "Frame Index"):
            costs.measure(MissingIndexClient(), "conditional", "GET", "/preview", [], 304)


    def test_transport_failure_retains_elapsed_time_without_secret_exception(self):
        class FailedClient:
            def request(self, *args):
                raise OSError("private credential and subject")

        samples = []
        with patch.object(costs.time, "perf_counter", side_effect=[1, 1.007]):
            with self.assertRaisesRegex(ValueError, "Measured transport request failed"):
                costs.measure(FailedClient(), "warm", "GET", "/private", samples, 200)
        self.assertAlmostEqual(7, samples[0]["milliseconds"])
        self.assertNotIn("private", str(samples))



if __name__ == "__main__":
    unittest.main()
