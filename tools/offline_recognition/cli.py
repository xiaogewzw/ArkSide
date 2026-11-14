"""JSON on stdout; errors on stderr. Suitable for a future .NET subprocess bridge."""

import argparse
import json
import sys
from pathlib import Path

from .domain import DEFAULT_DATA, RecognitionError, write_json


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="离线识别禁用盟约并筛选干员")
    parser.add_argument("image", type=Path)
    parser.add_argument("--data-dir", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--models-dir", type=Path)
    parser.add_argument("--output", type=Path, help="额外保存结果 JSON")
    parser.add_argument("--evidence-dir", type=Path, help="保存文字坐标、颜色判断及标注图")
    args = parser.parse_args()
    engine = None
    try:
        from .vision import Recognizer, read_image, save_image

        engine = Recognizer(args.data_dir, args.models_dir)
        recognition = engine.recognize(read_image(args.image))
        if args.output:
            write_json(args.output, recognition.result)
        if args.evidence_dir:
            write_json(args.evidence_dir / "evidence.json", recognition.evidence)
            save_image(args.evidence_dir / "annotated.png", recognition.annotated)
        print(json.dumps(recognition.result, ensure_ascii=False, indent=2))
        return 0
    except (RecognitionError, OSError, KeyError, ValueError) as exc:
        print(f"识别失败：{exc}", file=sys.stderr)
        return 2
    finally:
        diagnostics = getattr(engine, "diagnostics", None)
        if diagnostics is not None:
            # stderr remains diagnostic-only; stdout's result schema is stable.
            print("识别诊断：" + json.dumps(diagnostics, ensure_ascii=False), file=sys.stderr)
            if args.evidence_dir:
                write_json(args.evidence_dir / "diagnostics.json", diagnostics)


if __name__ == "__main__":
    sys.exit(main())
