# 开发与历史验收记录

> 本文件保存 v1.0.0 发布整理之前的开发说明和历史测试记录。界面名称、产物名称、工作流和部分状态属于当时版本，不作为当前使用说明或当前验收结论。当前用户说明见 [README](README.md)，构建与发布方法见 [发布指南](RELEASING.md)。

# ArkSide

2026-11-14 侧栏更新：连接状态与“开始识别”入口简化，识别期间显示进度动画；宽度固定，可拖动底部调整高度，默认显示两排头像。再次识别期间及失败后保留上次成功结果，成功后整体替换，底部“清空数据”可主动清除。当前侧栏不提供窗口设置入口；Windows 非默认进程的配置方法见 [Windows 正式版测试清单](Windows-正式版测试清单.md)。

2026-11-14 识别修复：增加盟约行列布局检测，支持当前游戏内“主盟约单行、副盟约两行”的页面。OCR 将相邻名称合并时，按实测网格切分区域重新识别，修复日志中“缺少伊比利亚、维多利亚”的失败。当前定格画面及 7 个缩放尺寸回放通过，原布局回归通过；正式包已重建。详细验证见 [离线识别记录](tools/offline_recognition/README.md)。按用户要求，最终验证为离线回放，未继续实时窗口测试。

正式版打包：

- Windows 当前版本测试：推送到 `codex/package-arkside` 自动运行 **Package ArkSide**，在成功的 Actions 运行页下载 `ArkSide-Windows-x64` 附件。校验并解压其中的应用 ZIP 后，按 [Windows 正式版测试清单](Windows-正式版测试清单.md) 验证；清单也随包附带。`ArkSide-Windows-verification` 附件保存自动测试、离线识别回放及启动检查报告。CI 成功后仍需完成真实游戏窗口测试。

- macOS Apple 芯片：运行 `tools/package-macos.sh`，得到 `artifacts/release/ArkSide.app` 和 `ArkSide-macOS-arm64.zip`。双击 ArkSide.app 启动，可移动到“应用程序”。按本次交付要求使用本地 ad-hoc 签名，未做 Developer ID 签名和公证。首次截图需要为 ArkSide 授予系统录屏权限。
- Windows x64：在 Windows 安装构建依赖后运行 `tools/package-windows.ps1`，或手动运行 GitHub Actions 的 **Package ArkSide** 工作流。得到 `ArkSide-Windows-x64.zip`，完整解压后双击 ArkSide 文件夹中的 `ArkSide.exe`。无需安装 .NET 或 Python，运行时、OCR 模型、干员数据和头像均已包含。
- 正式版只提供游戏连接、识别、干员推荐和筛选；截图展示、保存截图、连续预览、帧率、窗口诊断、JSON 路径和截图导入均不进入正式界面。正式构建关闭 `--demo`、`--operator-picker`、`--diagnostics`，不打包导入窗口与 Python 开发脚本。
- 异常提示：无截图权限为“没有截图权限”，其余截图问题为“截图异常”，识图问题为“请将游戏画面调整到所有盟约都显示出来”，干员资料问题为“干员详细资料异常”。技术细节仅写入本地日志。
- 正式版设置、目标配置及日志保存在用户的本地应用数据目录 `ArkSide` 下，无需写入应用包或安装目录。应用自带根目录 `ArkSide.png` 转换的 macOS / Windows 图标。可运行 `tools/prepare-icons.sh` 重新生成图标资源。

以下为开发与测试记录，其中诊断界面、截图导入、开发包路径等说明不适用于正式包。

# GameSidebar · B2 双平台侧边栏与截图

GameSidebar 是面向 Windows x64 和 Apple Silicon Mac 的窗口绑定与截图诊断程序，支持外部目标配置、真实窗口绑定、右侧侧边栏、单帧 PNG 和 1–5 FPS 预览，现已加入本地盟约识别与禁用干员列表。真实鼠标/键盘操作、长图属于后续批次。

2026-11-11：已将 [OpenCV + RapidOCR 离线识别](tools/offline_recognition/README.md) 接入 Avalonia 主程序侧边栏，点击“识别禁用干员”自动截图并显示禁用名单。已根据实际失败反馈修正低分辨率时取色范围误碰绿色说明文字的问题；当前通过 26 项回归、7 项发布包回放检查，以及发布包 C# 识别服务的 3 个实际调用用例。

## 运行

Windows 测试者可从 [B2 测试版发布页](https://github.com/xiaogewzw/ArkSide/releases/tag/b2-windows-acceptance-2026-09-30) 下载主程序和 TestWindow ZIP，按 [Windows B2 轻量测试清单](Windows-B2-测试清单.md) 验收。测试清单也附在主程序 ZIP 内；无需安装 SDK。Windows 包由 GitHub Windows runner 编译，截图与预览需真机另行确认。

使用 .NET SDK 10.0.401：

```sh
dotnet restore GameSidebar.sln --locked-mode
dotnet build GameSidebar.sln -c Release --no-restore
dotnet test GameSidebar.sln -c Release --no-build --no-restore
```

- Windows：运行发布 ZIP 内的 `GameSidebar.App.exe`。Maa Win32 Controller 使用 FramePool / PrintWindow 候选截取目标窗口；`--demo` 显式运行模拟窗口与模拟帧。
- macOS：运行 `tools/package-macos.sh` 生成 `artifacts/GameSidebar-Dev.app`，再用 `ditto artifacts/GameSidebar-Dev.app ~/Applications/GameSidebar-Dev.app` 安装到固定开发位置并从 Finder 启动。开发 Bundle ID 固定为 `dev.arkside.GameSidebar`。首次截图请在系统设置的“隐私与安全性 → 录屏与系统录音”允许此 App，必要时重新启动。应用选择、窗口枚举、位置由原生桥接提供；截图先用 Maa ScreenCaptureKit Controller，失败时尝试 ScreenCaptureKit 桥接，再使用系统窗口截图工具。侧边栏状态显示实际方法。
- Demo：`dotnet run --project src/GameSidebar.App -- --demo`，只用于 UI 和会话逻辑；不会自动代替真实窗口模式。

Windows 包与 Mac `.app` 都附 `target-game.example.json`，没有默认的活动 `target-game.json`。两平台游戏先由用户正常启动；可直接在候选窗口列表手动绑定。

## 侧边栏禁用干员识别

### 独立窗口与自动绑定（2026-11-13）

启动程序默认直接打开独立的“游戏助手”窗口。未配置外部目标时，macOS 使用明日方舟的 `com.hypergryph.arknights` Bundle ID，Windows 使用 `Arknights.exe` 文件名进行首次搜索；模拟器或其他客户端请在“窗口设置”中选择实际游戏窗口并保存目标。已有外部配置和旧设置优先，配置损坏时显示错误。

- 游戏已打开：启动后自动绑定，并跟随游戏窗口吸附在右侧。
- 游戏未打开：助手仍显示，提示“未找到明日方舟应用”；点击“重新搜索”会立即刷新搜索，找到后绑定。后台自动搜索也会继续。
- 游戏退出或窗口消失：助手留在原位置，若原来已收起则展开并显示未找到提示；已有干员推荐、禁用名单和筛选选择继续保留，截图和识别暂时禁用。
- “窗口设置”打开原诊断界面，关闭设置窗口只隐藏设置；关闭游戏助手才退出整个程序。也可用 `--diagnostics` 在启动时同时打开设置。

macOS 测试入口：双击 `tools/open-main-window.command`。可依次验证“先开游戏再开助手”“先开助手再开游戏并重新搜索”“已有识别结果时退出游戏”，最后重新打开游戏检查重新绑定与数据保留。

本轮验证：Release 构建和 Mac 打包成功；自动化测试 55 通过、1 个 Windows 原生 API 用例在 Mac 上跳过。已用真实明日方舟验证未运行提示与重试、游戏启动后自动绑定、游戏先打开时助手启动自动绑定、关闭设置窗口继续运行、退出游戏后助手保留并自动展开。干员推荐、截图和筛选在目标丢失及重新绑定后保留已通过 UI 调度器回归测试；真实游戏识别结果的退出保留可继续按上述流程验收。Mac 枚举已排除游戏创建的 66×20 等小型辅助窗口，避免将其当成第二个游戏候选。

### 主侧边栏标签推荐

主侧边栏已接入与导入弹窗相同的推荐功能。macOS 可双击 `tools/open-main-window.command` 启动主程序；自动绑定明日方舟后，进入盟约确认页面，点击右侧“识别干员推荐”，即可自动截图、识别并按 `tag` 显示头像分组。可选择具体副盟约及每组 3 人、6 人或全部。标签仍只在 JSON 中修改，在“禁用名单与数据”内点击“重新读取 JSON”生效。

“禁用名单与数据”保留禁用盟约、完整禁用名单和实际标签文件路径；“截图预览与设置”保留截图、保存、低帧率预览与权限入口。本次识别数据会一直保留，移动、缩放、最小化、换绑或解绑窗口不会清空推荐；只有点击“清空本次数据”或开始新识别才会清空旧数据。截图取得后，OCR 不受后续窗口变化影响；截图采集期间仍校验窗口，避免使用无效画面。手动清空会取消在途识别，迟到的结果不会恢复到界面。游戏内切换盟约后需要再次点击识别，预览不会自动更新推荐。

已完成编译、自动化检查与保存截图的离线回放；主侧边栏真实游戏验收由用户进行。

### 导入截图推荐弹窗（第一版）

主窗口点击“导入截图识别”，选择盟约确认页面的 PNG、JPG 或 BMP 图片（最大 32 MB），即可离线识别并查看结果。无需绑定游戏窗口或授予录屏权限。也可直接启动独立弹窗：

macOS 可双击 `tools/open-operator-picker.command`，优先打开 `artifacts/GameSidebar-Dev.app` 中的独立弹窗；没有打包应用时从源码启动。

```sh
dotnet run --project src/GameSidebar.App -c Release -- --operator-picker
# 可选：启动后自动导入一张已保存的图片
dotnet run --project src/GameSidebar.App -c Release -- --operator-picker --screenshot /绝对路径/截图.png
```

推荐按每个干员的 `tag` 字符串数组分组，如 `"tag": ["输出核心", "叠层核心"]`。在 `干员详细信息.json` 中，与 `operators` 同级的 `allowedTags` 是所有干员共用的标签白名单：

```json
{
  "allowedTags": ["输出核心", "叠层核心"],
  "operators": [
    {
      "id": "001",
      "name": "阿米娅",
      "tag": ["输出核心"],
      "tagCovenants": {},
      "portrait": "头像_阿米娅.png",
      "mainCovenant": [],
      "viceCovenant": ["奇迹"],
      "rank": 5
    }
  ]
}
```

只有与白名单匹配的标签才会出现在筛选和推荐分组中；干员 `tag` 中未登记或拼错的标签会被忽略（不改写原文件），空标签不进入推荐，但仍计入识别统计。匹配会去除首尾空白、去重，区分大小写；白名单缺失或为空时不显示任何标签，格式错误会提示读取失败。界面仅展示，不提供添加标签或移除干员的操作。选择“副盟约”后，只显示 `viceCovenant` 任意一项匹配且本次未禁用的干员；“全部副盟约”不限制盟约。各组保留 JSON 原顺序并显示全部匹配干员，支持 1 排 / 2 排切换；头像区域可用触控板横向滑动、鼠标按住拖动或底部滚动条浏览。没有匹配成员或全部禁用时给出相应提示。

头像下的副盟约按当前标签的 `tagCovenants` 显示，例如 `"tagCovenants": {"叠层核心": ["聚合"]}` 只显示聚合。未配置该标签（包括字段缺失或 `{}`）时显示全部 `viceCovenant`；空数组显示无适用副盟约。适用盟约必须属于该干员的 `viceCovenant`。副盟约筛选始终匹配完整 `viceCovenant`，不受 `tagCovenants` 影响：纯烬艾雅法拉可通过远见筛出，但叠层核心卡片只显示聚合。

主侧边栏与导入截图窗口均支持独立的 Rank 筛选（全部等级、Rank 1～6）。选择“叠层核心 + 全部副盟约 + 指定 Rank”可跨副盟约查看该等级的核心；选择具体副盟约后则与 Rank 取交集，并继续遵守本次禁用规则。切换标签、副盟约或显示排数会保留 Rank。各组人数在完整筛选后计算，显示全部结果。副盟约和 Rank 下拉框通过点击选择，滚轮或触控板滚动不会切换选项。头像下按姓名、Rank、副盟约排列。`rank` 必须为 1～6 的整数；旧数据缺少该字段时显示“Rank 未配置”，仅在“全部等级”中出现。修改 Rank 后点击“重新读取 JSON”即可重新筛选，无需再次识别。

运行时读取程序目录下的 `recognition/data/干员详细信息.json`；“数据与识别详情”显示实际路径。修改该文件的 `allowedTags`、`tag` 或 `tagCovenants` 后点击“重新读取 JSON”，即可使用当前识别结果重新筛选。修改干员名称、ID 或盟约归属需要重新导入截图；重新读取时会提示并保留本次使用的数据。JSON 读取失败也不会清空已有结果。源码中的 `干员数据/干员详细信息.json` 会在构建和打包时复制到输出目录，后续重新构建可能覆盖输出目录内的修改。头像从 `recognition/data/portraits` 读取，缺失头像显示占位文字。

识别期间可以取消；再次导入、失败或取消都会清空旧推荐，关闭弹窗会等待识别子进程结束。导入弹窗已由用户反馈测试可用；主侧边栏本轮完成编译、自动化检查和保存截图的离线回放，真实游戏测试待用户进行。

绑定游戏窗口后，进入游戏的盟约确认页面，点击侧边栏“识别干员推荐”。程序会重新截取当前画面，在本地识别禁用盟约，按干员库规则生成可滚动的禁用干员名单，并显示人数和截图时间。“截图预览与设置”可展开查看所用画面。

颜色判定通过名称定位取色区域，直接区分绿色激活和灰色禁用，无需检测圆形图标。取色尺度同时参考文字高度和单字宽度，避免低分辨率下 OCR 字框偏高而取到绿色说明文字；颜色判断按前景主色投票，容忍少量背景杂色。用户提供的新图原图、1097 像素复现版本及其他采集尺寸已实际回放验证。

识别期间按钮禁用并显示进度。识别失败时清空名单并显示原因；换绑、目标不可用、窗口位置或尺寸变化不会清空已完成的识别结果。截图尚未取得时会校验窗口是否变化，截图取得后的 OCR 可以继续完成。预览不会自动触发识别；游戏内更换盟约组合后应再次点击识别按钮。单次识别进程限时 60 秒，应用退出会取消任务并结束识别子进程。

Mac 打包脚本和 Windows CI 包含本地 OCR 组件、Python 运行时、模型和两份干员 JSON，无需用户额外安装 Python。首次准备构建环境：

```sh
python3.12 -m venv .venv-recognition
.venv-recognition/bin/python -m pip install -r tools/offline_recognition/requirements-build.txt
tools/package-macos.sh
```

源码直接 `dotnet run --project src/GameSidebar.App` 会自动查找本仓库的 `.venv-recognition`。其他开发环境可设置 `ARKSIDE_RECOGNITION_PYTHON` 为已安装识别依赖的 Python 绝对路径。主程序使用 Avalonia 展示列表，识别组件无需 PySide6；PySide6 仅用于之前的独立验证界面。

本次本机 Release 编译通过（0 警告、0 错误）；Mac 应用及识别组件已重新构建。已实际执行发布包识别组件，并通过发布包中的 C# 识别服务回放原样张、新图和低分辨率图，逐项比对盟约和干员名单。当前验证使用保存的真实游戏截图，没有操作游戏或重新采集实时窗口。Windows 原生识别组件由 Windows CI 构建，本机未执行 Windows 构建或运行。

## 目标配置

活动文件默认与 Windows exe 或 Mac `.app` 同级。开发目录或只读目录可用 `--target-config /绝对路径/target-game.json` 指定。UI 显示实际来源，可“重新加载”；编辑后“保存目标”写回当前外部文件，失败时可“另存目标配置”。另存到非默认位置后，下次启动需显式传 `--target-config`。

```json
{
  "schemaVersion": 1,
  "gameId": null,
  "displayName": null,
  "windows": {
    "executablePath": "D:\\Games\\ExampleGame\\Game.exe",
    "titleRule": null,
    "classRule": null,
    "preferredProfileId": "diagnostic-1920x1080"
  },
  "macOS": {
    "appBundlePath": null,
    "bundleId": null,
    "titleRule": null,
    "preferredProfileId": null
  }
}
```

Windows 要填写拥有游戏窗口的 exe **完整路径**；从运行窗口回填可避免误填启动器。明日方舟运行在模拟器中时，应选择实际承载游戏画面的模拟器窗口，从所选窗口回填路径后保存，再开始自动搜索。多个匹配窗口可手动选择，必要时增加标题规则。完整路径不可读时不按同名文件回退。macOS 可选择 `.app` 或从运行窗口回填 Bundle ID 和路径；应用移动后仍可按 Bundle ID 搜索运行中的窗口。

加载优先级是显式路径、同级 `target-game.json`、旧应用数据目录内 `settings.json` 的 target。前两项存在但损坏时会报错，不静默使用旧目标。外部文件当前平台字段为空时显示未配置。`settings.json` 仍保存旧目标兼容信息和其他设置；日志位置不变。个人活动配置已加入 `.gitignore`。

## 侧边栏与诊断

绑定真实窗口后，侧边栏在目标右侧出现。可收起至 48 DIP、展开至 300 DIP（头像高度 65 DIP，每排三个）、截图、选择路径保存 PNG、开始或停止低帧率预览。目标最小化、解绑或丢失时保持助手显示并暂停截图，恢复绑定后保留原识别数据。截图结果带会话代次与几何版本，换绑后的在途旧帧被丢弃。Profile 不匹配仍可预览窗口，布局分析和输入尚未开放。

macOS 窗口位置以 desktop point 记录，Retina 截图以 PNG 像素报告；Client 映射未知时界面明确标记未知。Windows 保留原有 Client 物理像素与 DPI 诊断。

## 当前验证状态（2026-09-30）

- Mac 本机构建、固定目录安装和 Finder 启动：已验证，构建/安装二进制哈希一致。
- 授权后的 Maa ScreenCaptureKit 已实际截取 Finder（1651×720）和明日方舟（1097×720），从应用保存 PNG 并检查完整画面。Finder 预览约 4 分钟，游戏预览约 3 分钟；停止、目标最小化/恢复、关闭/重新绑定、游戏退出和预览运行时程序退出均已实测。最终应用选择器修复版已再次授权，实际取得 Finder/游戏 PNG，Finder 和游戏预览复验均超过两分钟，停止与采集期间退出通过。本机详细证据记录含个人环境信息，留在本地。
- Windows x64：已交叉发布并检查 exe、空模板及 Maa Native 资产；Windows 真机 W1–W3 待验证。
- 自动测试：33 通过、1 个 Windows 原生 API 用例在 Mac 上跳过、0 失败；Release 构建 0 警告、0 错误。包含配置优先级、同名 exe 防误绑、旧帧丢弃、Native 在途停止/释放及停止期间重启保护。
- 远程 CI：每次推送由 [Build and test](https://github.com/xiaogewzw/ArkSide/actions/workflows/build.yml) 在 Mac/Windows 构建、自动测试并打包；发布工作流只接受成功构建中的 Windows ZIP，并校验 Native 资产、附测试清单和 SHA256。实际运行结果以对应提交的 Actions 记录为准。Windows 真机截图/预览仍待 [本轮清单](Windows-B2-测试清单.md) 验收；交叉构建和 ZIP 检查只代表产物检查。

开发包采用 ad-hoc 签名；重建可能使录屏授权失效。若开关显示开启但应用仍报告权限缺失，请先退出程序，在系统录屏列表移除旧条目，再用“+”明确添加固定安装位置的 `GameSidebar-Dev.app`，由用户亲自完成认证并退出重启，以实际目标帧确认授权成功。本轮曾通过此流程恢复真实截图；不要仅按开关状态判定。

如已授权却仍无法截取目标，请复制诊断 JSON，并记录侧边栏显示的实际方法、错误和窗口 ID。当前版本不保证全屏、跨 Spaces、Stage Manager、混合 DPI 或长时运行的兼容性。
