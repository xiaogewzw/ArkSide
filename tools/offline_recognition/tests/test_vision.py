import json
import unittest
from unittest.mock import patch

import cv2
import numpy as np

from ..domain import DEFAULT_DATA, RecognitionError, select_operators
from ..vision import Recognizer, classify_covenant_color, read_image


class VisionTests(unittest.TestCase):
    """Expected answers are read only by tests, never by the recognition engine."""

    @classmethod
    def setUpClass(cls):
        cls.image_path = DEFAULT_DATA / "识别测试/测试截图.png"
        cls.expected_path = DEFAULT_DATA / "识别测试/识别结果.json"
        cls.image = read_image(cls.image_path)
        cls.expected = json.loads(cls.expected_path.read_text(encoding="utf-8"))
        cls.engine = Recognizer()
        cls.baseline = cls.engine.recognize(cls.image)
        cls.variant_timings = {}

    def assert_variant(self, name, image):
        recognition = self.engine.recognize(image)
        self.variant_timings[name] = recognition.evidence["recognition_seconds"]
        self.assertEqual(recognition.result, self.expected)

    def test_sample_exact_json_including_list_order(self):
        self.assertEqual(self.baseline.result, self.expected)

    def test_all_labels_are_recognized_without_correction(self):
        labels = self.baseline.evidence["labels"]
        self.assertEqual(len(labels), 26)
        self.assertTrue(all(item["name"] == item["raw_text"] for item in labels))
        self.assertTrue(all(item["ocr_score"] >= .8 for item in labels))

    def test_user_screenshot_at_capture_resolution(self):
        image = read_image(DEFAULT_DATA / "识别测试/游戏内截图-02.png")
        expected = json.loads((DEFAULT_DATA / "识别测试/识别结果-02.json").read_text(encoding="utf-8"))
        # Reproduces the actual gray/green contamination failure at 1097px wide.
        image = cv2.resize(image, None, fx=.466, fy=.466, interpolation=cv2.INTER_AREA)
        self.assertEqual(self.engine.recognize(image).result, expected)

    def test_user_screenshot_original(self):
        image = read_image(DEFAULT_DATA / "识别测试/游戏内截图-02.png")
        expected = json.loads((DEFAULT_DATA / "识别测试/识别结果-02.json").read_text(encoding="utf-8"))
        self.assertEqual(self.engine.recognize(image).result, expected)

    def test_user_screenshot_capture_sizes(self):
        image = read_image(DEFAULT_DATA / "识别测试/游戏内截图-02.png")
        expected = json.loads((DEFAULT_DATA / "识别测试/识别结果-02.json").read_text(encoding="utf-8"))
        for width in (942, 1097, 1177, 1280, 1412, 1920):
            with self.subTest(width=width):
                resized = cv2.resize(image, (width, round(image.shape[0] * width / image.shape[1])),
                                     interpolation=cv2.INTER_AREA)
                self.assertEqual(self.engine.recognize(resized).result, expected)

    def test_gray_foreground_tolerates_small_green_heading_without_shape_detection(self):
        image = np.zeros((300, 400, 3), dtype=np.uint8)
        image[170:224, 165:215] = (140, 140, 140)
        image[160:163, 160:220] = (0, 210, 0)
        with patch.object(cv2, "HoughCircles", side_effect=AssertionError("Shape detection is forbidden")):
            result = classify_covenant_color(image, [160, 240, 220, 260])
        self.assertGreater(result["green_ratio"], .03)
        self.assertEqual(result["status"], "disabled")

    def test_scale_075(self):
        self.assert_variant("scale_075", cv2.resize(self.image, None, fx=.75, fy=.75, interpolation=cv2.INTER_AREA))

    def test_scale_125(self):
        self.assert_variant("scale_125", cv2.resize(self.image, None, fx=1.25, fy=1.25, interpolation=cv2.INTER_CUBIC))

    def test_window_title_and_outer_border_removed(self):
        # Fixture-specific crop is test input construction, not production geometry.
        self.assert_variant("client_crop", self.image[138:1555, 110:2373].copy())

    def test_image_translated_by_padding(self):
        image = cv2.copyMakeBorder(self.image, 60, 20, 90, 40, cv2.BORDER_CONSTANT, value=(20, 20, 20))
        self.assert_variant("translated", image)

    def test_brightness_080(self):
        self.assert_variant("brightness_080", cv2.convertScaleAbs(self.image, alpha=.8))

    def test_jpeg_quality_080(self):
        encoded = cv2.imencode(".jpg", self.image, [cv2.IMWRITE_JPEG_QUALITY, 80])[1]
        self.assert_variant("jpeg_080", cv2.imdecode(encoded, cv2.IMREAD_COLOR))

    def test_changed_icon_changes_bans_and_operator_lists(self):
        # Turn one previously green main icon gray without touching its name.
        # This checks real pixel dependence, rather than memorizing this fixture.
        source = next(label for label in self.baseline.evidence["labels"]
                      if label["kind"] == "mainCovenant" and label["status"] == "active")
        image = self.image.copy()
        x0, y0, x1, y1 = source["color_region"]
        gray = cv2.cvtColor(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), cv2.COLOR_GRAY2BGR)
        image[y0:y1, x0:x1] = gray[y0:y1, x0:x1]
        actual = self.engine.recognize(image).result
        bans = actual["识别到的禁用盟约"]
        self.assertEqual(set(bans["mainCovenant"]), set(self.expected["识别到的禁用盟约"]["mainCovenant"]) | {source["name"]})
        self.assertEqual(bans["viceCovenant"], self.expected["识别到的禁用盟约"]["viceCovenant"])
        self.assertEqual(actual, select_operators(self.engine.operators, bans, self.engine.options))
        self.assertGreater(actual["统计"]["被禁用干员数"], self.expected["统计"]["被禁用干员数"])

    def test_blank_image_is_rejected(self):
        with self.assertRaisesRegex(RecognitionError, "不完整"):
            self.engine.recognize(np.zeros_like(self.image))

    def test_missing_panel_is_rejected(self):
        with self.assertRaisesRegex(RecognitionError, "不完整"):
            self.engine.recognize(self.image[:1000].copy())

    def test_grayscale_image_is_rejected(self):
        image = cv2.cvtColor(cv2.cvtColor(self.image, cv2.COLOR_BGR2GRAY), cv2.COLOR_GRAY2BGR)
        with self.assertRaises(RecognitionError):
            self.engine.recognize(image)

    def test_uncertain_color_is_rejected(self):
        label = next(item for item in self.baseline.evidence["labels"] if item["status"] == "active")
        image = self.image.copy()
        hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
        colored = hsv[:, :, 1] > 100
        hsv[:, :, 0][colored] = 0  # Red is neither activated green nor disabled gray.
        image = cv2.cvtColor(hsv, cv2.COLOR_HSV2BGR)
        with self.assertRaisesRegex(RecognitionError, "颜色不确定"):
            classify_covenant_color(image, label["label_box"])

    def test_missing_local_model_is_rejected(self):
        import tempfile

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(RecognitionError, "缺少离线 OCR 模型"):
                Recognizer(models_dir=directory)
