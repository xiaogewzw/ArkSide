"""Manually read gray covenants; fixtures are never imported by the engine."""
import cv2

from ..domain import ROOT, select_operators
from ..vision import read_image

CAPTURES = (
    ('xnip', 'Xnip2026-11-14_17-28-38.png',
     ['维多利亚', '阿戈尔', '乌萨斯', '伊比利亚', '莱塔尼亚'],
     ['迅捷', '助力', '远见', '奇迹', '突袭']),
    ('windows-134711', '屏幕截图 2026-11-12 134711.png',
     ['谢拉格', '叙拉古', '卡西米尔', '伊比利亚', '莱塔尼亚'],
     ['精准', '迅捷', '助力', '远见', '不屈']),
    ('windows-232436', '屏幕截图 2026-11-12 232436.png',
     ['炎', '维多利亚', '谢拉格', '阿戈尔', '伊比利亚'],
     ['坚守', '助力', '远见', '聚合', '奥术']),
)


def real_capture_cases(operators, options):
    for name, filename, main, vice in CAPTURES:
        image = read_image(ROOT / 'windowsTest' / filename)
        expected = select_operators(operators, {'mainCovenant': main, 'viceCovenant': vice}, options)
        yield name, image, expected
        for width in (942, 1097, 1131, 1177, 1280, 1412, 1920):
            resized = cv2.resize(image, (width, round(image.shape[0] * width / image.shape[1])),
                                 interpolation=cv2.INTER_AREA)
            yield f'{name}-{width}', resized, expected
        # Remove only the screenshot's outer margin. The actual macOS capture
        # is 1097x720 and includes the title bar; this reproduces its geometry.
        crop = image[30:1514, 48:2308] if name == 'xnip' else image[39:, 2:-2]
        crop = cv2.resize(crop, (1097, round(crop.shape[0] * 1097 / crop.shape[1])),
                          interpolation=cv2.INTER_AREA)
        yield f'{name}-window-1097', crop, expected
