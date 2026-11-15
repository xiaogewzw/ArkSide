# 离线盟约识别验证

当前应用使用与发布说明见 [项目 README](../../README.md) 和 [发布指南](../../RELEASING.md)。本页保留算法说明与分阶段验证摘要；历史报告可在 Git 历史中查阅，新报告保存在本机 `artifacts/` 或对应 Actions 运行的 verification 附件中。

## 2026-11-14 三张真实截图的列对齐修复

`windowsTest/` 的 Xnip 图在原始分辨率可以识别，但缩放和窗口裁剪后会触发列间距偏差。17:28 的应用日志记录了 1097×720 输入：主盟约估算间距 76.5，完整副盟约行实际间距约 73.5，越靠右裁剪偏移越大，重试读成“准多利亚”“比利亚”“塔尼亚”。Xnip 的 1177 / 1280 像素版本及 Windows 134711 图的 942 / 1097 / 1131 / 1177 像素版本均复现了同类失败。

现在只在已读出的列中心能够对齐时，借用同面板至少六列连续完整行的实测列中心进行分格重试，不再把稀疏行的间距误差累积到右侧。重试范围仍受已识别名称及合并文字框约束，每格仍须实际 OCR 精确命中词库、置信度达到 0.80；不会从合并字符串或词库推定答案。旧版错位双行主盟约仍使用自己的布局。

回归用例在 `tests/real_capture_cases.py`，人工读取三张图的灰色盟约作为预期；每图覆盖原图、942 / 1097 / 1131 / 1177 / 1280 / 1412 / 1920 像素及去外边框的窗口尺寸，共 27 个输入，并核对完整结果 JSON（包括干员名单与顺序）。`verify` 与 `verify_packaged` 均包含这些用例；另检查擦除名称后必须拒绝，不能靠列位置补齐。该阶段实际验收：33 项离线回归、36 项发布包回放、6 项 C# 服务调用全部通过，对应 v4 历史报告。更新后的应用为 `artifacts/release/ArkSide.app`。Windows 截图在本机 macOS 离线回放，不等同于 Windows 原生运行验证。

2026-11-14：修复正式包对游戏内地图页面（主盟约单行、副盟约两行）的识别失败。日志中的“缺少伊比利亚、维多利亚”由 OCR 将两者合并成一个文本框引起。现先检测可见盟约的行、列间距与文字带；当名称不完整时，按检测到的网格切出空缺单元格，重新对实际像素执行 OCR。合并文本仅帮助确定重试范围，不直接拆词填充结果。名称仍须精确命中且置信度至少 0.80，缺行、缺名称或颜色不明确仍拒绝。原确认页的两行主盟约布局继续支持。

本次定格画面保存为 `游戏内截图-03.png`，人工读取禁用主盟约为维多利亚、谢拉格、卡西米尔、乌萨斯、莱塔尼亚；副盟约为迅捷、奇迹、突袭、不屈、奥术。原图及 942 / 1097 / 1131 / 1177 / 1280 / 1412 / 1920 像素宽度均回放成功。31 项离线回归通过，正式包组件 9 项检查通过（含不完整输入拒绝）；对应 v3 历史报告。CLI 的 stderr 与主程序日志现包含布局、OCR 原文、重试区域与前后缺失名称，stdout 结果格式不变。本次最终验证使用保存画面的离线回放；按用户要求未继续实际窗口测试。

2026-11-11 已在本机 Apple Silicon Mac / Python 3.12.15 上验证：OpenCV + RapidOCR + PySide6 可以完成提供样张的识别。实际生成的结果与 `干员数据/识别测试/识别结果.json` 的整个 JSON 对象相等，包括数组顺序。

| 项目 | 实际结果 |
| --- | --- |
| 禁用主盟约 | 谢拉格、伊比利亚、叙拉古、维多利亚、阿戈尔 |
| 禁用副盟约 | 坚守、助力、远见、聚合、奥术 |
| 干员总数 | 141 |
| 禁用 / 可选 | 57 / 84 |
| 文字识别 | 26 个盟约全部精确命中词库，无纠错替换 |
| 自动检查 | 26 项通过，0 失败，0 错误 |
| 网络 | 系统禁止联网时通过，Python 网络访问尝试为 0 |

现已接入 .NET / Avalonia 主程序侧边栏：点击“识别禁用干员”即可截取绑定窗口，调用同一套离线识别逻辑，并在侧边栏显示可滚动的禁用干员名单。主程序不依赖 PySide6；PySide6 继续用于独立验证界面的导入、粘贴、标注、明细和 JSON 导出。

当前版本为 `text_anchored_hsv_v2`：没有圆形图标检测，直接按 OCR 名称采样绿色/灰色。实际失败复现于新截图缩到 1097 像素宽：偏高的文字框将取色范围扩到了上方绿色说明文字，导致灰色“卡西米尔”中有约 4% 绿色而报不确定。现通过单字宽度约束取色尺度、缩小向上取色范围，并按前景主色判断；此版本已实际运行回归与发布包检查，对应 v2 历史报告。

新图 `游戏内截图-02.png` 的人工标注：禁用主盟约为卡西米尔、伊比利亚、莱塔尼亚、乌萨斯、阿戈尔；副盟约为精准、灵巧、坚守、奇迹、奥术。根据干员库规则得到 57 名禁用、84 名可选，逐项结果保存在 `识别结果-02.json`，仅测试读取。

## 主程序构建与接入

主程序通过 `IOperatorRecognizer` 调用本地 CLI，传递临时 PNG 和数据目录，读取 UTF-8 JSON 并校验统计与名单一致性。识别限时 60 秒。截图采集期间校验目标窗口；取得截图后，OCR 可继续处理已有画面，换绑和游戏丢失不清空已完成的推荐。再次识别期间及失败后保留上次成功结果，成功后整体替换；手动清空或应用退出会取消在途任务，临时截图在任务结束后删除。

Windows 发布包中 `recognition/worker/ArkSide.Recognition.exe` 包含 Python、依赖和 OCR 模型。Mac 对应目录为 `.app/Contents/Resources/recognition/worker/`，避免 Python 的原生库目录被应用签名器误判为代码包。主程序旁的 `recognition/data/` 包含默认 JSON 模板和头像（Mac 在 `Contents/MacOS/recognition/data/`）。正式应用将缺失的两份 JSON 初始化到用户本地应用数据目录 `ArkSide/recognition/data`，已有文件保持原样；推荐列表与 OCR 的 `--data-dir` 使用该外部目录，头像仍读取包内资源。自定义标签不修改应用包。路径、恢复默认和升级说明见项目 README。

开发构建继续读取输出目录中的数据，同时复制核心 Python 源码，可使用仓库内的 `.venv-recognition`，或通过 `ARKSIDE_RECOGNITION_PYTHON` 指定解释器绝对路径。打包回放默认使用包内模板，另由 `verify_external_data` 在临时外部目录验证实际 C# 服务和冻结 worker 共用自定义 JSON，并检查包内模板未变化。

```sh
# 只构建主程序和离线组件，不执行 OCR。
.venv-recognition/bin/python -m pip install -r tools/offline_recognition/requirements-build.txt
tools/package-macos.sh

# Windows 发布到目标目录后，在 Windows 本机执行：
python -m pip install -r tools/offline_recognition/requirements-build.txt
python -m tools.offline_recognition.build_worker \
  --output artifacts/GameSidebar-win-x64/recognition/worker
```

PyInstaller 无法跨平台构建该原生组件，Mac / Windows 必须各自在对应平台打包。仓库的两平台 CI 已加入打包步骤。本次已在 Mac 构建并实际执行组件，另调用发布包中的 `OfflineOperatorRecognizer` 服务确认 C# 到子进程的传参、UTF-8 JSON 解析及禁用名单；没有操作实时游戏窗口。

## 安装和运行

从项目根目录执行，推荐 Python 3.12。依赖已锁定为本次实测版本。

```sh
python3.12 -m venv .venv-recognition
.venv-recognition/bin/python -m pip install -r tools/offline_recognition/requirements.txt

# 打开验证界面，也可使用界面中的“打开截图”或“粘贴截图”。
.venv-recognition/bin/python -m tools.offline_recognition.gui \
  --image 干员数据/识别测试/测试截图.png

# 无界面识别：stdout 为 UTF-8 JSON，错误写入 stderr，失败退出码为 2。
.venv-recognition/bin/python -m tools.offline_recognition.cli \
  干员数据/识别测试/测试截图.png \
  --output artifacts/offline-recognition/result.json \
  --evidence-dir artifacts/offline-recognition

# 完整验收，包括与提供结果逐项对比。
.venv-recognition/bin/python -m tools.offline_recognition.verify
```

Windows 对应的解释器路径是 `.venv-recognition\Scripts\python.exe`。本次尚未实测 Windows 安装、Qt 界面和执行耗时。

命令支持 `--data-dir` 指定包含两份干员 JSON 的目录。CLI / GUI 也支持 `--models-dir` 指定本地模型目录，目录内应保留下述三个模型文件名。数据默认路径根据源码所在位置解析，不依赖截图路径。

## 识别流程和规则

1. 从 `干员禁用规则.json` 加载 10 个主盟约和 16 个副盟约的名称词库，从 `干员详细信息.json` 加载干员；校验重复 ID、重复名字和未知盟约。
2. RapidOCR 在 CPU 上运行本地 PP-OCRv4，精确匹配词库。根据已读出的名称检测行列布局，支持单行和分组双行主盟约。当名称不完整且同行锚点支持规则列间距时，对空缺或合并的单元格分区重新 OCR，并映射回原图坐标。每个名字必须唯一、文字置信度至少为 0.80；重试后仍缺失任何一个名称就报错。不会按位置、词库顺序或预期答案补齐名称。
3. 以名称文字框为锚点，取同一行文字高度中位数与单字宽度中位数的 1.10 倍中的较小者作为尺度，采样文字上方 0.4–4.0 倍尺度的矩形区域；横向不跨过相邻名称中心的中点。无需检测圆形、边缘、轮廓或图标样式。运行时代码没有样张的固定像素坐标。
4. 直接统计 HSV 颜色：V≥60 的像素参与前景投票，绿色为 H=55–105、S≥120，灰白为 S≤100（OpenCV H 范围 0–179）。绿色占区域至少 8% 且占前景至少 55% 为激活；绿色占前景不超过 20%、灰白占区域至少 4% 且占前景至少 65% 为禁用。其余情况提示颜色不确定，避免把纯黑背景当作灰色禁用。标注图显示实际取色区域，证据记录 `color_method=text_anchored_hsv_v2`、`color_region` 及前景投票比例。
5. 每种盟约至少需要检测到一个绿色激活盟约；主盟约名称应排列在副盟约上方。拒绝灰度图、不完整页面、低置信度文字和无法确定的颜色，不输出默认可选名单。
6. 非空 `mainCovenant` 只判断主盟约，全部被禁用才禁用干员。主盟约为空才判断 `viceCovenant`，副盟约非空且全部被禁用才禁用干员。两者皆空可选。名单保持干员库顺序，禁用盟约保持画面阅读顺序。

规则文件的两段自然语言规则已实现为 `domain.py` 中的确定性逻辑；修改自然语言描述本身不会自动生成新的判断逻辑。

识别代码 `vision.py` 和 `domain.py` 不读取样张或 `识别结果.json`。预期结果只用于 `tests/test_vision.py` 验收；正常 CLI 和 GUI 可以接收其他截图。

## 离线模型与部署

验证使用 `rapidocr-onnxruntime==1.4.4`，其安装包自带模型，推理不下载模型，不使用大模型服务。运行时明确加载本地路径，缺文件直接报错，并关闭 ONNX Runtime 遥测。使用的文件为：

- `ch_PP-OCRv4_det_infer.onnx`
- `ch_ppocr_mobile_v2.0_cls_infer.onnx`
- `ch_PP-OCRv4_rec_infer.onnx`

实际文件大小、SHA256 和依赖版本可通过 CLI 的 `--evidence-dir` 参数导出到本地 `artifacts/`。OCR 是本地的小型识别模型，离线部署仍需要携带它们。

安装依赖通常需要联网；要在离线机器安装，先在相同操作系统、CPU 架构及 Python 版本的准备机器上下载依赖：

```sh
python -m pip download -r tools/offline_recognition/requirements.txt \
  --only-binary=:all: --dest artifacts/offline-wheelhouse
```

将代码、`干员数据` 和 wheelhouse 一起复制到目标机器，在新建虚拟环境内执行：

```sh
python -m pip install --no-index --find-links /path/to/offline-wheelhouse \
  -r tools/offline_recognition/requirements.txt
```

这是独立工具的离线安装准备方法；尚未测试离线重装。主程序发布包已改为携带识别运行时、依赖和模型，侧边栏将非零退出码作为识别失败处理，并在取消、超时和退出时结束进程。

## 验证证据与边界

历史阶段的实际验收摘要（macOS 离线回放）：

| 阶段 | 离线回归 | 发布包回放 | C# 服务调用 |
| --- | --- | --- | --- |
| v2：文字锚定取色修复 | 26 项通过 | 7 项通过 | 3 项通过 |
| v3：游戏内单行主盟约布局修复 | 31 项通过 | 9 项通过 | 3 项通过 |
| v4：三张真实截图的列对齐修复 | 33 项通过 | 36 项通过 | 6 项通过 |

生成的历史 JSON 报告已从当前源码移除，仍可在 Git 历史中查阅。识别回归需要的原始截图与预期结果继续保留。完整尺寸标注图、界面截图及重新运行产生的记录统一保存在项目根目录 `artifacts/offline-recognition/` 或 `artifacts/release-checks/`；这些目录不进入 Git。CI 报告见对应 Actions 运行的 verification 附件。

26 项检查包含规则优先级与多盟约语义、JSON 完全相等、75% / 125% 缩放、平移、去窗口标题与边框、80% 亮度、JPEG quality=80、新图原图、1097 像素复现版本，以及 942 / 1097 / 1177 / 1280 / 1412 / 1920 像素采集尺寸。另用包含少量绿色杂色的灰色区域验证不依赖形状检测，改变激活区域为灰色后应改变禁用名单；空图、缺面板、灰度图、异常颜色、缺离线模型都应拒绝。

发布包回放从实际组件目录运行冻结后的可执行文件，使用和 C# 相同的工作目录及参数，并使用含中文和空格的输入路径。原图和 1097 像素版的两张样张、新图去标题栏的 1097 像素版及 JPEG 版共 6 个用例比较完整 JSON；第 7 个空图用例检查失败退出码和空 stdout。另用发布包的 C# 服务实际识别原样张、新图和新图 1097 像素版，比较所有禁用干员名称、顺序、盟约和统计。

重跑发布包回放：

```sh
sandbox-exec -p '(version 1) (allow default) (deny network*)' \
  .venv-recognition/bin/python -m tools.offline_recognition.verify_packaged \
  --app-dir artifacts/GameSidebar-Dev.app/Contents/MacOS
```

重跑发布包的 C# 服务调用核对（先构建发布包，并完成上述回放以生成 1097 像素输入）：

```sh
dotnet run --project tools/GameSidebar.RecognitionCheck -c Release -- \
  "$PWD/artifacts/GameSidebar-Dev.app/Contents/MacOS" \
  "$PWD/artifacts/offline-recognition/packaged/dotnet-service.json" \
  "$PWD/干员数据/识别测试/测试截图.png" "$PWD/干员数据/识别测试/识别结果.json" \
  "$PWD/干员数据/识别测试/游戏内截图-02.png" "$PWD/干员数据/识别测试/识别结果-02.json" \
  "$PWD/artifacts/offline-recognition/packaged/user-1097/含 空格的输入/游戏截图.png" "$PWD/干员数据/识别测试/识别结果-02.json"
```

该独立工具直接引用发布包 DLL，不加入主解决方案的常规构建。Windows 默认引用 `artifacts/GameSidebar-win-x64`；其他发布位置需同时用 `-p:RecognitionAppDirectory=...` 指定编译引用目录，并将同一目录作为第一个运行参数。

本机实际用 macOS 系统网络沙箱执行验收，同时在 Python 层拦截网络函数：

```sh
sandbox-exec -p '(version 1) (allow default) (deny network*)' \
  .venv-recognition/bin/python -m tools.offline_recognition.verify

QT_QPA_PLATFORM=offscreen \
sandbox-exec -p '(version 1) (allow default) (deny network*)' \
  .venv-recognition/bin/python -m tools.offline_recognition.gui \
  --image 干员数据/识别测试/测试截图.png \
  --smoke-dir artifacts/offline-recognition
```

另用相同系统沙箱中的 socket 连接作为对照，确认连接被 `PermissionError: Operation not permitted` 拒绝。Qt 验收从后台线程真正执行 OCR，检查 57 / 84 个名单行、26 行盟约明细，并保存界面截图和结果 JSON。

当前有两张不同禁用组合的独立样张，变体由这两张图生成。约 1 秒的单张识别耗时不含 Python 进程启动、依赖导入和界面启动，不能代表 Windows 或其他机器性能。尚不能据此覆盖所有禁用组合、改版 UI、HDR/夜间滤镜、旋转/透视图片、动画中间帧或不同游戏页面。应继续补充两平台真实截图，优先验证未知输入的拒绝行为。

技术参考：[RapidOCR 源码](https://github.com/RapidAI/RapidOCR)、[Qt 线程与信号示例](https://doc.qt.io/qtforpython-6/examples/example_widgets_thread_signals.html)。
