"""Read the shared version and record the source of each distributable."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]


def version():
    value = ET.parse(ROOT / "Version.props").findtext("./PropertyGroup/Version", "")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?", value):
        raise ValueError(f"Version.props 中的版本无效：{value!r}")
    return value


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("version")
    tag = commands.add_parser("check-tag")
    tag.add_argument("tag")
    manifest = commands.add_parser("manifest")
    manifest.add_argument("--platform", choices=("macOS-arm64", "Windows-x64"), required=True)
    manifest.add_argument("--output", type=Path, required=True)
    sums = commands.add_parser("checksums")
    sums.add_argument("directory", type=Path)
    args = parser.parse_args()
    current_version = version()
    if args.command == "version":
        print(current_version)
    elif args.command == "check-tag":
        if args.tag != f"v{current_version}":
            parser.error(f"标签 {args.tag!r} 与 Version.props 的 v{current_version} 不一致")
        print(current_version)
    elif args.command == "manifest":
        data = {
            "version": current_version,
            "source_commit": git("rev-parse", "HEAD"),
            "source_dirty": bool(git("status", "--porcelain", "--untracked-files=no")),
            "platform": args.platform,
            "self_contained": True,
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    elif args.command == "checksums":
        # Only accept the two products for this exact version; never mix old ZIPs.
        lines = []
        commits = set()
        for platform in ("macOS-arm64", "Windows-x64"):
            archive = args.directory / f"ArkSide-v{current_version}-{platform}.zip"
            metadata = json.loads((args.directory / f"{platform}-release.json").read_text(encoding="utf-8"))
            if metadata["version"] != current_version or metadata["platform"] != platform or metadata["source_dirty"]:
                parser.error(f"{platform} 的构建来源或版本无效")
            embedded_path = ("ArkSide.app/Contents/Resources/release.json" if platform == "macOS-arm64"
                             else "ArkSide/release.json")
            with zipfile.ZipFile(archive) as package:
                embedded = json.loads(package.read(embedded_path))
            if embedded != metadata:
                parser.error(f"{platform} 的 ZIP 内来源记录与附件不一致")
            commits.add(metadata["source_commit"])
            checksum = hashlib.sha256()
            with archive.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    checksum.update(chunk)
            lines.append(f"{checksum.hexdigest()}  {archive.name}\n")
        if commits != {git("rev-parse", "HEAD")}:
            parser.error("两个平台的产物必须来自当前检出的同一个提交")
        (args.directory / "SHA256SUMS.txt").write_text("".join(lines), encoding="ascii")


if __name__ == "__main__":
    main()
