"""Verify a published C# recognizer and catalog use editable JSON outside the app."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from tempfile import TemporaryDirectory

from .domain import ROOT, select_operators, write_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    app_dir = args.app_dir.resolve()
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    bundled = app_dir / "recognition/data"
    names = ("干员详细信息.json", "干员禁用规则.json")
    before = {name: hashlib.sha256((bundled / name).read_bytes()).hexdigest() for name in names}
    roster = json.loads((bundled / names[0]).read_text(encoding="utf-8"))
    rules = json.loads((bundled / names[1]).read_text(encoding="utf-8"))
    baseline = json.loads((ROOT / "干员数据/识别测试/识别结果.json").read_text(encoding="utf-8"))
    # A different roster size detects accidental use of the bundled JSON by
    # either the C# catalog or the actual frozen OCR worker.
    roster["operators"] = roster["operators"][:-1]
    roster["allowedTags"] = ["外部配置验证"]
    roster["operators"][0]["tag"] = ["外部配置验证"]
    expected = select_operators(roster["operators"], baseline["识别到的禁用盟约"], rules["covenantOptions"])
    expected_path = output_dir / "expected.json"
    write_json(expected_path, expected)
    with TemporaryDirectory(prefix="ArkSide 外部配置 ") as temporary:
        external = Path(temporary) / "recognition/data"
        external.mkdir(parents=True)
        write_json(external / names[0], roster)
        custom = (external / names[0]).read_bytes()
        # Leave the rules missing, so this also exercises first-run seeding.
        service_report = output_dir / "service.json"
        subprocess.run([
            "dotnet", "run", "--project", "tools/GameSidebar.RecognitionCheck/GameSidebar.RecognitionCheck.csproj",
            "-c", "Release", f"-p:RecognitionAppDirectory={app_dir}", "--",
            "--data-dir", str(external), str(app_dir), str(service_report),
            str(ROOT / "干员数据/识别测试/测试截图.png"), str(expected_path),
        ], cwd=ROOT, check=True)
        report = json.loads(service_report.read_text(encoding="utf-8-sig"))
        checks = {
            "external_catalog_and_worker": report["passed"] and report["data_source"] == "external"
                and report["catalog_operators"] == len(roster["operators"]),
            "custom_tags_loaded": report["catalog_tags"] == ["外部配置验证"],
            "portraits_from_bundle": report["portraits_from_bundle"],
            "custom_json_preserved": (external / names[0]).read_bytes() == custom,
            "missing_rules_seeded": (external / names[1]).read_bytes() == (bundled / names[1]).read_bytes(),
            "bundle_json_unchanged": all(hashlib.sha256((bundled / name).read_bytes()).hexdigest() == before[name]
                                         for name in names),
        }
    result = {"passed": all(checks.values()), "checks": checks}
    write_json(output_dir / "verification.json", result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
