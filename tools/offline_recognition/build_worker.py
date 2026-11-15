"""Build the offline worker for the current OS/architecture, without running OCR."""

import argparse
from pathlib import Path
import platform
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description="打包本平台离线 OCR 组件（只构建，不运行识别）")
    parser.add_argument("--output", type=Path, required=True, help="应用目录中的 recognition/worker 目录")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.name != "worker" or output.parent.name != "recognition":
        parser.error("输出目录必须以 recognition/worker 结尾，避免覆盖其他文件")
    architecture = f"{sys.platform}-{platform.machine()}"
    build = ROOT / "artifacts/recognition-build" / architecture
    command = [sys.executable, "-m", "PyInstaller", "--noconfirm", "--onedir",
               "--name", "ArkSide.Recognition", "--paths", str(ROOT),
               "--distpath", str(build / "dist"), "--workpath", str(build / "work"),
               "--specpath", str(build), "--collect-data", "rapidocr_onnxruntime",
               "--collect-binaries", "onnxruntime"]
    # Keep distribution metadata and bundled license files for runtime packages,
    # their dependencies, and the PyInstaller bootloader distribution.
    runtime_packages = [line.strip().split("==", 1)[0]
                        for line in (ROOT / "tools/offline_recognition/requirements-core.txt").read_text().splitlines()
                        if line.strip() and not line.lstrip().startswith("#")]
    for package in (*runtime_packages, "pyinstaller"):
        command.extend(["--recursive-copy-metadata", package])
    for package in ("PySide6", "PyQt5", "PyQt6", "tkinter", "matplotlib"):
        command.extend(["--exclude-module", package])
    command.append(str(ROOT / "tools/offline_recognition/worker_entry.py"))
    subprocess.run(command, check=True, cwd=ROOT)
    bundle = build / "dist/ArkSide.Recognition"
    # This is a generated worker directory with a fixed name, not an application installation.
    if output.exists():
        shutil.rmtree(output)
    shutil.copytree(bundle, output, symlinks=True)
    print(f"离线识别组件已构建：{output}")


if __name__ == "__main__":
    main()
