"""Reproduce acceptance checks and save machine-readable evidence."""

import argparse
from contextlib import ExitStack
import hashlib
from pathlib import Path
import platform
import socket
import sys
import time
import unittest
from unittest.mock import patch

from .domain import ROOT, write_json


class RecordingResult(unittest.TextTestResult):
    def startTest(self, test):
        self.started = time.perf_counter()
        super().startTest(test)

    def addSuccess(self, test):
        self.records.append({"test": test.id(), "passed": True,
                             "seconds": round(time.perf_counter() - self.started, 4)})
        super().addSuccess(test)

    def addFailure(self, test, err):
        self.records.append({"test": test.id(), "passed": False, "error": self._exc_info_to_string(err, test)})
        super().addFailure(test, err)

    def addError(self, test, err):
        self.records.append({"test": test.id(), "passed": False, "error": self._exc_info_to_string(err, test)})
        super().addError(test, err)

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.records = []


def main():
    parser = argparse.ArgumentParser(description="核对离线识别结果及鲁棒性并保存验证报告")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "artifacts/offline-recognition")
    args = parser.parse_args()
    network_attempts = []

    def deny_network(*args, **kwargs):
        network_attempts.append("blocked")
        raise RuntimeError("离线验收禁止任何网络访问")

    started = time.perf_counter()
    with ExitStack() as stack:
        for target in ("socket.socket.connect", "socket.socket.connect_ex", "socket.create_connection", "socket.getaddrinfo"):
            stack.enter_context(patch(target, deny_network))
        from .tests import test_domain, test_vision
        from .vision import save_image

        suite = unittest.TestSuite(unittest.defaultTestLoader.loadTestsFromModule(module)
                                   for module in (test_domain, test_vision))
        result = unittest.TextTestRunner(verbosity=2, resultclass=RecordingResult).run(suite)
        baseline = getattr(test_vision.VisionTests, "baseline", None)
        if baseline:
            write_json(args.output_dir / "result.json", baseline.result)
            write_json(args.output_dir / "evidence.json", baseline.evidence)
            save_image(args.output_dir / "annotated.png", baseline.annotated)
        report = {
            "passed": result.wasSuccessful() and not network_attempts,
            "test_count": result.testsRun, "failures": len(result.failures), "errors": len(result.errors),
            "python_network_attempts": len(network_attempts),
            "platform": platform.platform(), "python": platform.python_version(),
            "total_seconds": round(time.perf_counter() - started, 4),
            "sample_exact_match": bool(baseline and baseline.result == test_vision.VisionTests.expected),
            "sample_sha256": hashlib.sha256(test_vision.VisionTests.image_path.read_bytes()).hexdigest(),
            "expected_sha256": hashlib.sha256(test_vision.VisionTests.expected_path.read_bytes()).hexdigest(),
            "variant_recognition_seconds": getattr(test_vision.VisionTests, "variant_timings", {}),
            "tests": result.records,
        }
        write_json(args.output_dir / "verification.json", report)
    print(f"验证{'通过' if report['passed'] else '失败'}，报告：{args.output_dir / 'verification.json'}")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
