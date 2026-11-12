"""PySide6 verification UI. Run OCR on a worker thread, keep results exportable."""

import argparse
import sys
from pathlib import Path

import cv2
import numpy as np
from PySide6.QtCore import QThread, QTimer, Qt, Signal, Slot
from PySide6.QtGui import QColor, QImage, QPixmap
from PySide6.QtWidgets import (
    QApplication, QFileDialog, QHBoxLayout, QHeaderView, QLabel, QListWidget,
    QMainWindow, QPushButton, QSplitter, QTabWidget, QTableWidget,
    QTableWidgetItem, QVBoxLayout, QWidget,
)

from .domain import DEFAULT_DATA, write_json
from .vision import Recognizer, read_image


class RecognitionJob(QThread):
    succeeded = Signal(object)
    failed = Signal(str)

    def __init__(self, image, data_dir, models_dir, parent=None):
        super().__init__(parent)
        self.image, self.data_dir, self.models_dir = image, data_dir, models_dir

    def run(self):
        try:
            result = Recognizer(self.data_dir, self.models_dir).recognize(self.image)
            self.succeeded.emit(result)
        except Exception as exc:
            self.failed.emit(str(exc))


class RecognitionWindow(QMainWindow):
    def __init__(self, data_dir=DEFAULT_DATA, models_dir=None, smoke_dir=None):
        super().__init__()
        self.data_dir, self.models_dir = data_dir, models_dir
        self.smoke_dir = smoke_dir
        self.job = self.recognition = self.preview_pixmap = None
        self.close_when_finished = False
        self.setWindowTitle("ArkSide · 离线盟约识别验证")
        self.resize(1320, 900)
        root = QWidget()
        self.setCentralWidget(root)
        layout = QVBoxLayout(root)
        controls = QHBoxLayout()
        self.open_button = QPushButton("打开截图")
        self.open_button.clicked.connect(self.choose_image)
        self.paste_button = QPushButton("粘贴截图")
        self.paste_button.clicked.connect(self.paste_image)
        self.export_button = QPushButton("导出结果 JSON")
        self.export_button.clicked.connect(self.export_result)
        self.export_button.setEnabled(False)
        for button in (self.open_button, self.paste_button, self.export_button):
            controls.addWidget(button)
        controls.addStretch()
        layout.addLayout(controls)
        self.status = QLabel("打开或粘贴完整的盟约确认页面截图。所有识别均在本地执行。")
        self.status.setWordWrap(True)
        layout.addWidget(self.status)
        self.bans = QLabel("")
        self.bans.setWordWrap(True)
        layout.addWidget(self.bans)
        splitter = QSplitter(Qt.Orientation.Horizontal)
        layout.addWidget(splitter, 1)
        self.preview = QLabel("截图预览")
        self.preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.preview.setMinimumSize(400, 350)
        self.preview.setStyleSheet("background:#15191f;color:#c4c8ce;border-radius:6px;")
        splitter.addWidget(self.preview)
        tabs = QTabWidget()
        self.tabs = tabs
        self.disabled_list, self.available_list = QListWidget(), QListWidget()
        tabs.addTab(self.disabled_list, "禁用干员")
        tabs.addTab(self.available_list, "可选干员")
        self.covenants = QTableWidget(0, 4)
        self.covenants.setHorizontalHeaderLabels(["盟约", "状态", "文字置信度", "绿色占比"])
        self.covenants.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.covenants.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        tabs.addTab(self.covenants, "识别明细")
        splitter.addWidget(tabs)
        splitter.setSizes([850, 470])

    def set_preview(self, image):
        rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        qimage = QImage(rgb.data, rgb.shape[1], rgb.shape[0], rgb.strides[0], QImage.Format.Format_RGB888).copy()
        self.preview_pixmap = QPixmap.fromImage(qimage)
        self.fit_preview()

    def fit_preview(self):
        if self.preview_pixmap is not None:
            self.preview.setPixmap(self.preview_pixmap.scaled(
                self.preview.size(), Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation))

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if hasattr(self, "preview"):
            self.fit_preview()

    @Slot()
    def choose_image(self):
        path, _ = QFileDialog.getOpenFileName(self, "选择游戏截图", "", "截图 (*.png *.jpg *.jpeg *.bmp)")
        if path:
            self.open_image(Path(path))

    def clear_results(self):
        self.recognition = None
        self.export_button.setEnabled(False)
        self.disabled_list.clear()
        self.available_list.clear()
        self.covenants.setRowCount(0)
        self.bans.setText("")
        self.tabs.setTabText(0, "禁用干员")
        self.tabs.setTabText(1, "可选干员")

    def open_image(self, path):
        self.clear_results()
        try:
            self.start_recognition(read_image(path))
        except Exception as exc:
            self.failed(str(exc))

    @Slot()
    def paste_image(self):
        self.clear_results()
        image = QApplication.clipboard().image()
        if image.isNull():
            self.failed("剪贴板中没有截图")
            return
        image = image.convertToFormat(QImage.Format.Format_RGBA8888)
        pixels = np.frombuffer(image.bits(), dtype=np.uint8).reshape(image.height(), image.bytesPerLine())
        rgba = pixels[:, :image.width() * 4].reshape(image.height(), image.width(), 4)
        self.start_recognition(cv2.cvtColor(rgba, cv2.COLOR_RGBA2BGR))

    def start_recognition(self, image):
        self.set_preview(image)
        self.open_button.setEnabled(False)
        self.paste_button.setEnabled(False)
        self.status.setText("正在本地识别盟约与图标状态…")
        self.job = RecognitionJob(image, self.data_dir, self.models_dir, self)
        self.job.succeeded.connect(self.show_result)
        self.job.failed.connect(self.failed)
        self.job.finished.connect(self.job_finished)
        self.job.start()

    @Slot(object)
    def show_result(self, recognition):
        self.recognition = recognition
        result = recognition.result
        counts = result["统计"]
        self.status.setText(
            f"识别完成：{counts['干员总数']} 名干员 · 禁用 {counts['被禁用干员数']} 名 · "
            f"可选 {counts['可以选用干员数']} 名 · {recognition.evidence['recognition_seconds']:.2f} 秒")
        bans = result["识别到的禁用盟约"]
        self.bans.setText(f"禁用主盟约：{'、'.join(bans['mainCovenant']) or '无'}\n"
                          f"禁用副盟约：{'、'.join(bans['viceCovenant']) or '无'}")
        self.disabled_list.addItems(result["被禁用干员"])
        self.available_list.addItems(result["可以选用干员"])
        self.tabs.setTabText(0, f"禁用干员 ({self.disabled_list.count()})")
        self.tabs.setTabText(1, f"可选干员 ({self.available_list.count()})")
        labels = recognition.evidence["labels"]
        self.covenants.setRowCount(len(labels))
        for row, label in enumerate(labels):
            disabled = label["status"] == "disabled"
            values = (label["name"], "禁用" if disabled else "激活",
                      f"{label['ocr_score']:.1%}", f"{label['green_ratio']:.1%}")
            for column, value in enumerate(values):
                item = QTableWidgetItem(value)
                item.setForeground(QColor("#c83c3c" if disabled else "#21804c"))
                self.covenants.setItem(row, column, item)
        self.set_preview(recognition.annotated)
        self.export_button.setEnabled(True)

    @Slot(str)
    def failed(self, message):
        self.clear_results()
        self.status.setText(f"识别失败：{message}")
        if self.smoke_dir:
            write_json(self.smoke_dir / "gui-smoke.json", {"passed": False, "error": message})
            if self.job is None or not self.job.isRunning():
                QTimer.singleShot(0, lambda: QApplication.instance().exit(2))

    @Slot()
    def job_finished(self):
        self.open_button.setEnabled(True)
        self.paste_button.setEnabled(True)
        self.job.deleteLater()
        self.job = None
        if self.close_when_finished:
            self.close()
        elif self.smoke_dir:
            QTimer.singleShot(150, self.finish_smoke)

    @Slot()
    def finish_smoke(self):
        if self.recognition is None:
            QApplication.instance().exit(2)
            return
        self.smoke_dir.mkdir(parents=True, exist_ok=True)
        result = self.recognition.result
        write_json(self.smoke_dir / "gui-result.json", result)
        captured = self.grab().save(str(self.smoke_dir / "gui.png"))
        passed = (self.disabled_list.count() == result["统计"]["被禁用干员数"]
                  and self.available_list.count() == result["统计"]["可以选用干员数"]
                  and self.covenants.rowCount() == len(self.recognition.evidence["labels"])
                  and self.export_button.isEnabled() and captured)
        write_json(self.smoke_dir / "gui-smoke.json", {
            "passed": passed, "disabled_rows": self.disabled_list.count(),
            "available_rows": self.available_list.count(), "covenant_rows": self.covenants.rowCount(),
            "worker_finished": self.job is None, "screenshot_saved": captured,
        })
        QApplication.instance().exit(0 if passed else 2)

    @Slot()
    def export_result(self):
        if self.recognition is None:
            return
        path, _ = QFileDialog.getSaveFileName(self, "导出识别结果", "识别结果.json", "JSON (*.json)")
        if path:
            try:
                write_json(Path(path), self.recognition.result)
                self.status.setText(f"结果已导出：{path}")
            except OSError as exc:
                self.status.setText(f"导出失败：{exc}")

    def closeEvent(self, event):
        if self.job is not None and self.job.isRunning():
            self.close_when_finished = True
            self.status.setText("本地识别即将完成，完成后关闭窗口…")
            event.ignore()
        else:
            event.accept()


def main():
    parser = argparse.ArgumentParser(description="PySide6 离线识别验证界面")
    parser.add_argument("--image", type=Path)
    parser.add_argument("--data-dir", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--models-dir", type=Path)
    parser.add_argument("--smoke-dir", type=Path, help="自动识别、保存界面及结果并退出（验收用）")
    args = parser.parse_args()
    if args.smoke_dir and not args.image:
        parser.error("--smoke-dir 需要 --image")
    app = QApplication(sys.argv[:1])
    window = RecognitionWindow(args.data_dir, args.models_dir, args.smoke_dir)
    window.show()
    if args.image:
        QTimer.singleShot(0, lambda: window.open_image(args.image))
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
