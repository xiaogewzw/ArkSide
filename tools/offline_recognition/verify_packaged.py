"""Replay screenshots through the actual bundled worker with production arguments."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import cv2

from .domain import DEFAULT_DATA, ROOT, write_json
from .vision import read_image, save_image


def main():
    parser = argparse.ArgumentParser(description="实际运行发布包中的识别组件并核对完整结果")
    parser.add_argument("--app-dir", type=Path, required=True, help="主程序可执行文件所在目录，Mac 为 Contents/MacOS")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "artifacts/offline-recognition/packaged")
    args = parser.parse_args()
    app_dir, output_dir = args.app_dir.resolve(), args.output_dir.resolve()
    recognition_dir = app_dir / "recognition"
    worker = recognition_dir / "worker" / ("ArkSide.Recognition.exe" if sys.platform == "win32" else "ArkSide.Recognition")
    if sys.platform == "darwin" and not worker.is_file():
        worker = app_dir.parent / "Resources/recognition/worker/ArkSide.Recognition"
    if not worker.is_file():
        parser.error(f"识别组件不存在：{worker}")
    fixtures = DEFAULT_DATA / "识别测试"
    cases = []
    for name, filename, expected_name in (("original", "测试截图.png", "识别结果.json"),
                                           ("user", "游戏内截图-02.png", "识别结果-02.json")):
        image = read_image(fixtures / filename)
        expected = json.loads((fixtures / expected_name).read_text(encoding="utf-8"))
        cases.append((name, image, expected))
        scaled = cv2.resize(image, (1097, round(image.shape[0] * 1097 / image.shape[1])), interpolation=cv2.INTER_AREA)
        cases.append((name + "-1097", scaled, expected))
        if name == "user":
            crop = image[94:1512, 48:2304].copy()  # Fixture construction only, not runtime geometry.
            crop = cv2.resize(crop, (1097, round(crop.shape[0] * 1097 / crop.shape[1])), interpolation=cv2.INTER_AREA)
            cases.append(("user-client-1097", crop, expected))
            compressed = cv2.imdecode(cv2.imencode(".jpg", scaled, [cv2.IMWRITE_JPEG_QUALITY, 80])[1], cv2.IMREAD_COLOR)
            cases.append(("user-jpeg-1097", compressed, expected))
    records = []
    for name, image, expected in cases:
        case_dir = output_dir / name
        image_path = case_dir / "含 空格的输入" / "游戏截图.png"
        result_path = case_dir / "result.json"
        save_image(image_path, image)
        # Match the .NET ProcessStartInfo working directory and argument list.
        process = subprocess.run([
            str(worker), str(image_path), "--data-dir", str(recognition_dir / "data"),
            "--output", str(result_path), "--evidence-dir", str(case_dir),
        ], cwd=recognition_dir, capture_output=True, encoding="utf-8", timeout=60)
        passed = False
        error = process.stderr.strip()
        if process.returncode == 0:
            try:
                passed = (json.loads(process.stdout) == expected
                          and json.loads(result_path.read_text(encoding="utf-8")) == expected)
            except (ValueError, OSError) as exc:
                error = str(exc)
        records.append({"case": name, "passed": passed, "exit_code": process.returncode,
                        "image_size": [image.shape[1], image.shape[0]], "error": error})
        print(f"{name}: {'PASS' if passed else 'FAIL'}", flush=True)
    # Exercise the production failure path as well: no usable JSON on stdout.
    blank = cases[0][1].copy()
    blank[:] = 0
    blank_path = output_dir / "blank.png"
    save_image(blank_path, blank)
    process = subprocess.run([str(worker), str(blank_path), "--data-dir", str(recognition_dir / "data")],
                             cwd=recognition_dir, capture_output=True, encoding="utf-8", timeout=60)
    records.append({"case": "invalid-input", "passed": process.returncode == 2 and not process.stdout,
                    "exit_code": process.returncode, "error": process.stderr.strip()})
    report = {"passed": all(case["passed"] for case in records), "cases": records,
              "executed_at_utc": datetime.now(timezone.utc).isoformat(),
              "worker_sha256": hashlib.sha256(worker.read_bytes()).hexdigest(),
              "process_invocation": "production worker, working directory and argument list",
              "source_version": "text_anchored_hsv_v2"}
    write_json(output_dir / "verification.json", report)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
