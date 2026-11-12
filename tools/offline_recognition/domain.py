"""Data validation and deterministic operator selection, independent of OCR/Qt."""

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DATA = ROOT / "干员数据"
KINDS = ("mainCovenant", "viceCovenant")


class RecognitionError(ValueError):
    """The input cannot produce a sufficiently reliable selection."""


def load_data(directory=DEFAULT_DATA):
    directory = Path(directory)
    options = json.loads((directory / "干员禁用规则.json").read_text(encoding="utf-8"))["covenantOptions"]
    operators = json.loads((directory / "干员详细信息.json").read_text(encoding="utf-8"))["operators"]
    names = [name for kind in KINDS for name in options[kind]]
    if not names or len(names) != len(set(names)):
        raise RecognitionError("盟约名称为空或重复")
    ids, op_names = set(), set()
    for op in operators:
        if op["id"] in ids or op["name"] in op_names:
            raise RecognitionError("干员 ID 或名称重复")
        ids.add(op["id"])
        op_names.add(op["name"])
        for kind in KINDS:
            if not isinstance(op[kind], list) or not set(op[kind]) <= set(options[kind]):
                raise RecognitionError(f"{op['name']} 的 {kind} 不在盟约词库中")
    return options, operators


def is_disabled(operator, disabled):
    # A nonempty main covenant takes precedence, even if vice covenants are banned.
    kind = "mainCovenant" if operator["mainCovenant"] else "viceCovenant"
    values = operator[kind]
    return bool(values) and set(values).issubset(disabled[kind])


def select_operators(operators, disabled, options):
    for kind in KINDS:
        unknown = set(disabled[kind]) - set(options[kind])
        if unknown:
            raise RecognitionError(f"未知盟约：{sorted(unknown)}")
    banned, available = [], []
    for operator in operators:
        (banned if is_disabled(operator, disabled) else available).append(operator["name"])
    return {
        "识别到的禁用盟约": disabled,
        "统计": {"干员总数": len(operators), "被禁用干员数": len(banned), "可以选用干员数": len(available)},
        "被禁用干员": banned,
        "可以选用干员": available,
    }


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
