"""OCR labels and measure activation color in text-anchored image regions.

Only covenant vocabulary comes from the data files. No fixture, expected result,
operator name, banned list, or screenshot coordinate is used by this module.
"""

import hashlib
import time
from dataclasses import dataclass
from importlib import metadata
from pathlib import Path

import cv2
import numpy as np
import onnxruntime
import rapidocr_onnxruntime
from rapidocr_onnxruntime import RapidOCR

from .domain import DEFAULT_DATA, KINDS, RecognitionError, load_data, select_operators


MODEL_NAMES = (
    "ch_PP-OCRv4_det_infer.onnx",
    "ch_ppocr_mobile_v2.0_cls_infer.onnx",
    "ch_PP-OCRv4_rec_infer.onnx",
)


def dependency_versions():
    versions = {}
    for key in ("rapidocr_onnxruntime", "opencv-python", "onnxruntime", "numpy", "PySide6"):
        try:
            versions[key] = metadata.version(key)
        except metadata.PackageNotFoundError:
            versions[key] = None  # The main application's headless worker does not need Qt.
    return versions


@dataclass
class Recognition:
    result: dict
    evidence: dict
    annotated: np.ndarray


def read_image(path):
    # imdecode/fromfile supports Chinese filenames on Windows as well.
    try:
        image = cv2.imdecode(np.fromfile(Path(path), dtype=np.uint8), cv2.IMREAD_COLOR)
    except (OSError, cv2.error) as exc:
        raise RecognitionError(f"无法读取截图：{path}") from exc
    if image is None:
        raise RecognitionError(f"无法解码截图：{path}")
    return image


def save_image(path, image):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    ok, encoded = cv2.imencode(path.suffix, image)
    if not ok:
        raise RecognitionError(f"无法编码图像：{path}")
    encoded.tofile(path)


def label_rows(labels):
    """Group slightly misaligned text boxes into rows, then read left to right."""
    rows = []
    for item in sorted(labels, key=lambda x: x["label_box"][1]):
        y = item["label_box"][1]
        height = item["label_box"][3] - y
        if not rows or abs(y - rows[-1][0]["label_box"][1]) > height * 1.2:
            rows.append([])
        rows[-1].append(item)
    return [sorted(row, key=lambda x: x["label_box"][0]) for row in rows]


def reading_order(labels):
    return [item for row in label_rows(labels) for item in row]


def classify_covenant_color(image, label_box, text_height=None, horizontal_limits=None):
    """Read green/gray color above a known name without detecting any shape.

    A row's normalized character size keeps short/long OCR boxes on one scale.
    Neighboring text centers bound the region so another covenant cannot vote.
    Dark background and black glyph interiors do not vote as gray foreground.
    """
    left, top, right, bottom = label_box
    h = max(4.0, text_height if text_height is not None else bottom - top)
    cx = (left + right) / 2
    x0, x1 = cx - 1.5 * h, cx + 1.5 * h
    if horizontal_limits is not None:
        x0, x1 = max(x0, horizontal_limits[0]), min(x1, horizontal_limits[1])
    x0, x1 = max(0, int(x0)), min(image.shape[1], int(np.ceil(x1)))
    y0, y1 = max(0, int(top - 4.0 * h)), min(image.shape[0], int(top - 0.4 * h))
    if x1 <= x0 or y1 <= y0:
        raise RecognitionError("盟约颜色区域不在截图内，请保留完整盟约面板")
    patch = image[y0:y1, x0:x1]
    pixels = cv2.cvtColor(patch, cv2.COLOR_BGR2HSV).reshape(-1, 3)
    visible = pixels[:, 2] >= 60
    green_pixels = (visible & (pixels[:, 0] >= 55) & (pixels[:, 0] <= 105)
                    & (pixels[:, 1] >= 120))
    neutral_pixels = visible & (pixels[:, 1] <= 100)
    green, neutral = float(np.mean(green_pixels)), float(np.mean(neutral_pixels))
    visible_count = int(np.count_nonzero(visible))
    green_foreground = float(np.count_nonzero(green_pixels) / max(1, visible_count))
    neutral_foreground = float(np.count_nonzero(neutral_pixels) / max(1, visible_count))
    # Vote by dominant foreground color. A little green from nearby explanatory
    # text/edge glow must not invalidate a clearly gray covenant (the 1097px bug).
    if green >= 0.08 and green_foreground >= 0.55:
        status = "active"
    elif green_foreground <= 0.20 and neutral >= 0.04 and neutral_foreground >= 0.65:
        status = "disabled"
    else:
        raise RecognitionError(f"盟约颜色不确定：绿色占比 {green:.3f}，灰白占比 {neutral:.3f}")
    return {"status": status, "color_region": [x0, y0, x1, y1],
            "green_ratio": round(green, 6), "neutral_ratio": round(neutral, 6),
            "green_foreground_ratio": round(green_foreground, 6),
            "neutral_foreground_ratio": round(neutral_foreground, 6)}


class Recognizer:
    def __init__(self, data_dir=DEFAULT_DATA, models_dir=None):
        self.options, self.operators = load_data(data_dir)
        self.name_kind = {name: kind for kind in KINDS for name in self.options[kind]}
        directory = Path(models_dir) if models_dir else Path(rapidocr_onnxruntime.__file__).parent / "models"
        paths = [directory / name for name in MODEL_NAMES]
        for path in paths:
            if not path.is_file():
                raise RecognitionError(f"缺少离线 OCR 模型：{path}；请在安装阶段准备模型")
        onnxruntime.disable_telemetry_events()
        started = time.perf_counter()
        self.ocr = RapidOCR(
            det_model_path=str(paths[0]), cls_model_path=str(paths[1]), rec_model_path=str(paths[2]),
            intra_op_num_threads=2, inter_op_num_threads=2,
            det_limit_side_len=2000, det_limit_type="max", max_side_len=2600,
        )
        self.init_seconds = time.perf_counter() - started
        self.model_info = [{"file": p.name, "bytes": p.stat().st_size,
                            "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in paths]

    def recognize(self, image):
        if not isinstance(image, np.ndarray) or image.ndim != 3 or image.shape[2] != 3:
            raise RecognitionError("输入必须是 BGR 截图")
        started = time.perf_counter()
        raw, elapsed = self.ocr(image, use_cls=False)
        labels, seen = [], set()
        for box, text, score in raw or []:
            name = "".join(text.split())
            if name not in self.name_kind:
                continue
            if name in seen:
                raise RecognitionError(f"重复识别到盟约：{name}，请仅保留一个游戏面板")
            if score < 0.80:
                raise RecognitionError(f"盟约 {name} 的文字置信度不足：{score:.3f}")
            seen.add(name)
            points = np.asarray(box)
            bounds = [float(points[:, 0].min()), float(points[:, 1].min()),
                      float(points[:, 0].max()), float(points[:, 1].max())]
            labels.append({"name": name, "kind": self.name_kind[name], "raw_text": text,
                           "ocr_score": round(float(score), 6), "label_box": bounds})
        missing = sorted(set(self.name_kind) - seen)
        if missing:
            raise RecognitionError(f"盟约面板识别不完整，缺少：{'、'.join(missing)}")
        rows = label_rows(labels)
        for row in rows:
            height = float(np.median([item["label_box"][3] - item["label_box"][1] for item in row]))
            character_width = float(np.median([
                (item["label_box"][2] - item["label_box"][0]) / len(item["name"]) for item in row
            ]))
            # OCR's detection padding varies sharply across resolutions. Width
            # per Chinese character is a second font-size estimate, preventing
            # tall boxes from extending sampling up into the green page heading.
            height = min(height, 1.10 * character_width)
            centers = [(item["label_box"][0] + item["label_box"][2]) / 2 for item in row]
            for index, label in enumerate(row):
                limits = ((centers[index - 1] + centers[index]) / 2 if index else 0,
                          (centers[index] + centers[index + 1]) / 2 if index + 1 < len(row) else image.shape[1])
                try:
                    label.update(classify_covenant_color(image, label["label_box"], height, limits))
                except RecognitionError as exc:
                    raise RecognitionError(f"{label['name']}：{exc}") from exc
        labels = [item for row in rows for item in row]
        disabled = {kind: [label["name"] for label in labels
                           if label["kind"] == kind and label["status"] == "disabled"] for kind in KINDS}
        # A grayscale capture cannot establish activation color. Refuse all-gray inputs.
        for kind in KINDS:
            if not any(label["kind"] == kind and label["status"] == "active" for label in labels):
                raise RecognitionError(f"{kind} 未发现绿色激活盟约，请检查截图颜色或面板状态")
        main_bottom = max(label["label_box"][3] for label in labels if label["kind"] == KINDS[0])
        vice_top = min(label["label_box"][1] for label in labels if label["kind"] == KINDS[1])
        if main_bottom >= vice_top:
            raise RecognitionError("盟约区域排列异常，请使用本局信息确认页面")
        annotated = image.copy()
        for index, label in enumerate(labels, 1):
            color = (40, 50, 240) if label["status"] == "disabled" else (80, 220, 50)
            sx0, sy0, sx1, sy1 = label["color_region"]
            cv2.rectangle(annotated, (sx0, sy0), (sx1, sy1), color, 2)
            x0, y0, x1, y1 = (round(v) for v in label["label_box"])
            cv2.rectangle(annotated, (x0, y0), (x1, y1), color, 2)
            cv2.putText(annotated, f"{index:02} {label['status'].upper()}", (sx0, max(15, sy0 - 8)),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 2)
        evidence = {
            "color_method": "text_anchored_hsv_v2",
            "image_size": [image.shape[1], image.shape[0]], "labels": labels,
            "model_init_seconds": round(self.init_seconds, 4),
            "recognition_seconds": round(time.perf_counter() - started, 4),
            "ocr_stage_seconds": elapsed, "models": self.model_info,
            "versions": dependency_versions(),
            "execution_provider": "CPUExecutionProvider",
        }
        return Recognition(select_operators(self.operators, disabled, self.options), evidence, annotated)
