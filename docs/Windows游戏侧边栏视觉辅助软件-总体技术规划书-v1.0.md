# Windows 游戏侧边栏视觉辅助软件  
## 总体技术规划书 V1.1（评审修订版）

**规划日期：** 2026-09-29  
**修订日期：** 2026-09-29  
**目标平台：** Windows x64  
**主要开发环境：** macOS Apple Silicon M5  
**主要技术栈：** C# / .NET 10 / Avalonia 12 / MaaFramework / OpenCvSharp / GitHub Actions


**版本说明：** 为保持现有引用，文件名保留 `v1.0`，正文版本升级为 V1.1。本版已合并设计评审 R01–R12，作为后续开发基线；设计修订不代表代码实现或真机验收已经完成。

**术语：** V1 Framework 指 Phase 0–10 的完整范围；Phase 1 专指窗口绑定诊断程序。目标游戏名称、进程名和匹配规则允许暂空。Phase 0–1 的任务分解见同目录 `Phase0-1-开发清单.md`。

---

# 1. 项目定位

本项目是一个运行于 Windows 的独立游戏视觉辅助程序。

程序绑定指定游戏窗口，在游戏窗口右侧生成一个独立的侧边栏，通过视觉截图获取游戏界面内容，在本地完成图像分析，并在侧边栏展示处理结果。

对于部分需要交互的功能，程序可以在用户主动触发后临时将游戏置于前台并接管鼠标，对游戏执行点击、拖动等常规用户操作，再获取新的游戏画面继续分析。

项目的长期设计目标不是实现某一个固定业务，而是建立一个稳定的：

**窗口绑定 + 截图 + 视觉处理 + 自动化 + UI 展示 + Feature 扩展**

基础框架，使后续新的游戏辅助功能可以快速开发。

---

# 2. 产品边界

## 2.1 V1 Framework 的支持目标

- Windows 11 x64；Windows 10 22H2 x64 保留为项目自行验证的兼容目标。
- 窗口化、无边框窗口模式；固定一个目标游戏，多显示器及 Windows DPI 缩放。
- 指定的 Client Area 物理像素尺寸与已验证游戏布局。
- 未最小化窗口的后台视觉截图；用户主动触发的前台自动化。
- 上下、左右的一维滚动采集、本地图像处理及本地诊断数据。

不能只根据某个截图 API 的最低系统版本推导整个技术栈的支持范围。当前 .NET 10 官方支持表未列出普通 Windows 10 22H2；项目兼容目标、官方支持和实际测试通过必须分别记录，Windows 11 也需记录实际版本/edition。[.NET 10 平台支持](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

## 2.2 Phase 1 的交付边界

Phase 1 交付窗口发现、手动选择、绑定、Profile 尺寸校验、几何跟踪、失效恢复和诊断主窗口。右侧侧边栏属于 Phase 2，截图属于 Phase 3，首次输入能力与紧急停止一起在 Phase 4 交付。

目标游戏尚未配置时：自动发现停用，手动选窗和 Demo 可用；使用 diagnostic-only Profile 验证窗口系统。工程验收与真实游戏验收分别记录，不能用测试窗口通过代替真实游戏兼容性结论。

## 2.3 明确不做

V1 不实现独占全屏、游戏内存读取、DLL/Process Injection、游戏 API Hook、内核驱动、驱动级输入、网络封包读取或修改、反作弊绕过、后台自动化、二维扫描、第三方 DLL 插件、云端识别、账号、遥测、截图自动上传、自动更新或安装程序。

最小化期间暂停截图与自动化，不把最小化截图纳入 V1。程序使用普通用户权限，不自动提权；更高权限目标的输入不属于支持范围。外部视觉与正常输入方式不等同于目标游戏允许自动化。

---

# 3. 技术基线

| 项目 | 基线 |
| --- | --- |
| Language / Runtime | C# / .NET 10 |
| UI | Avalonia 12，MVVM |
| Architecture | Application Services + Core + 平台适配器 + 后续 Feature Modules |
| Capture / Input | MaaFramework C# Binding，Phase 3 起接入 |
| Vision | OpenCvSharp，在算法首次引入时接入 |
| Development | macOS Apple Silicon arm64，Windows x64 真机验证 |
| Distribution | Self-contained win-x64 ZIP，非 Single File |

Avalonia 桌面平台支持本项目采用的 .NET 基线，Windows 默认支持每显示器 DPI，布局单位为 DIP。[平台要求](https://docs.avaloniaui.net/docs/supported-platforms)、[Windows 指南](https://docs.avaloniaui.net/docs/platform-specific-guides/windows)

版本与构建规则：

- `global.json` 锁定具体 .NET 10 SDK，`Directory.Packages.props` 固定直接 NuGet 版本；启用并提交 lock files，CI 使用 locked restore。
- 不使用 floating/latest/nightly 依赖。SDK、托管包、Native 包的配套关系写入依赖基线；不能假设 Maa Binding 与 Native 版本号必须相同。
- OpenCvSharp 明确选定主版本、托管包及 win-x64 / osx.arm64 Runtime；先在 Mac 和 Windows 验证加载、Mat、编解码与简单运算，再确认算法开发环境可用。[官方包选择](https://github.com/shimat/opencvsharp/blob/main/docs/docfx/articles/getting-started/package-selection.md)
- Phase 0–1 四个生产项目统一 `net10.0`。Windows 实现标注平台限制，由组合根按运行平台注册；Mac 不执行 Windows 初始化。RID 仅在对应还原/发布步骤指定，不全局强制 win-x64。
- 初期关闭 Single File、Trimming 和 AOT，避免增加 Native 集成变量。
- CI 固定 runner 镜像大版本并记录修订，不以 `windows-latest` 代表固定环境。

本规划不编造尚未验证的具体依赖版本；锁版本和验证属于 Phase 0 及各依赖首次接入任务。

---

# 4. 总体架构

整体采用分层架构：

```text
┌────────────────────────────────────────────┐
│                Avalonia UI                 │
│ Sidebar / Settings / Developer Mode        │
├────────────────────────────────────────────┤
│              Application Layer             │
│ GameSession / Feature / Automation         │
│ Analysis Coordinator / Capture Coordinator │
├────────────────────────────────────────────┤
│                  Core                      │
│ Models / Contracts / Profiles / State      │
├───────────────┬──────────────┬─────────────┤
│ Maa Adapter   │ Vision       │ Windows     │
│ Capture/Input │ OpenCV       │ HWND/DPI    │
├───────────────┴──────────────┴─────────────┤
│              Windows / Game                │
└────────────────────────────────────────────┘
```

依赖方向必须保持：

```text
UI
 ↓
Application
 ↓
Core

Infrastructure ──→ Core/Application
```

Core 层永远不得反向依赖 Avalonia、MaaFramework 或 Win32。

---

# 5. Solution 结构

Phase 0–1 只建立以下四个生产项目：

```text
GameSidebar.sln
src/
  GameSidebar.App/                Views / ViewModels / Composition / Development / Storage
  GameSidebar.Core/               Sessions / Geometry / Profiles / Result Models
  GameSidebar.Application/        Abstractions / Discovery / Sessions
  GameSidebar.Platform.Windows/   Interop / Discovery / Geometry

tests/
  GameSidebar.Core.Tests/
  GameSidebar.Application.Tests/
  GameSidebar.Platform.Windows.Tests/

tools/
  GameSidebar.TestWindow/         受控 Windows 验收窗口，不随正式应用默认发布

assets/profiles/
docs/testing/
```

App 的 Composition Root 引用并注册具体平台实现；Views / ViewModels 只能调用应用接口。平台端口放在 Application，纯数据与规则放在 Core。Core 不暴露 Avalonia、Maa、OpenCV 或 Win32 对象。

后续按实际需要增加：

| 阶段 | 项目或目录 |
| --- | --- |
| Phase 3 | Infrastructure.Maa：MaaHost / Controller / Capture / 后续 Input；Infrastructure：Fixture / Storage / Logging |
| 首次图像算法 | Vision：ROI、匹配、稳定检测，后续增加拼接 |
| Phase 6–8 | Vision.Tests、Replay.Tests、assets/fixtures、templates |
| Feature 增长后 | Features.Builtin |

Fake 窗口服务暂放 App/Development，测试替身放测试工程。不提前建立大量空项目或仅用于转发的抽象层。

---

# 6. GameSession 与绑定生命周期

`GameSessionManager` 是当前绑定状态的唯一写入者，UI 和 Feature 只读取不可变 `GameSessionSnapshot`。快照不执行截图、输入、识别或 UI 更新。

| 字段 | 语义 |
| --- | --- |
| SessionId | 每次接受新绑定时生成；窗口丢失后重新绑定必须更新 |
| BindingGeneration | Manager 生命周期内单调递增；换绑、解绑、丢失或停止旧工作时先递增，拒绝迟到结果 |
| WindowIdentity | 不透明 WindowId、PID、可获取的进程创建时间、身份核验结果 |
| Geometry / GeometryVersion | 当前几何及版本；位置、尺寸、显示器或映射相关 DPI 变化时递增 |
| ProfileValidation | 尺寸/配置校验结果，与真实游戏兼容性分开 |
| BindingMode / AutoDiscoveryEnabled | Auto / Manual，与是否继续自动搜索分开记录 |
| TargetCompatibility | Unknown / Verified / Unsupported；Phase 1 诊断目标默认 Unknown |
| State / SelectionKind | 连接状态；待选择时区分 Window / Profile |
| IsForeground / IsMinimized | 独立运行状态，不作为游戏兼容性结论 |
| ObservedAt / Error | 采样时间、有效性和可诊断错误 |

WindowId 只在运行期使用，Native HWND 由平台适配器解析；业务不解释或持久化句柄。诊断可以显示 HWND 的字符串表示，但不能用它恢复下一次绑定。

每次查询及正式操作前核对窗口归属 PID、可用的进程创建时间和匹配信息。`IsWindow` 只能辅助检查：HWND 会复用，检查后仍可能销毁。[IsWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindow)

身份信息不足或暂时读失败时标为未充分验证，允许展示诊断，但不能进入 Ready 或执行输入。发现身份不一致则立即使旧代次失效。重复检查降低竞态风险，不能保证外部窗口身份检查与操作完全原子化。

连接状态：

```text
Starting
  ├─ 无目标配置 → NotConfigured
  ├─ 配置无效 → Faulted（InvalidConfiguration）
  └─ 有效配置 → Searching
Searching
  ├─ 0 个候选 → Searching
  ├─ 多候选 → NeedsSelection（Window）
  └─ 唯一候选 → Binding → Validating
手动选择 → Binding → Validating
Validating
  ├─ 身份/几何有效且 Profile 尺寸匹配 → Ready
  ├─ 尺寸不匹配 → UnsupportedResolution
  ├─ 多个 Profile 无法唯一确定 → NeedsSelection（Profile，保留绑定）
  ├─ 最小化、身份未充分验证或几何暂不可用 → BoundUnavailable
  ├─ 配置损坏/不支持 schema → Faulted（InvalidConfiguration）
  └─ 窗口无效 → GameLost
任一有绑定状态
  ├─ 几何/配置恢复 → 重新 Validating
  ├─ 窗口丢失 → GameLost → 自动模式、自动搜索仍启用且规则有效则 Searching，否则 Unbound
  └─ 用户解绑 → Unbound，停止自动重绑
Faulted → 修复配置/显式重试
应用关闭 → 使旧代次失效，取消搜索与跟踪，等待在途工作结束
```

`Ready` 仅表示绑定身份、几何和尺寸校验有效，不代表截图、输入、游戏布局已适配。UnsupportedResolution、BoundUnavailable 及等待 Profile 选择期间仍跟踪已绑定窗口；恢复后重新验证。不可恢复服务故障进入 Faulted，不无限重试。

换绑先使旧代次失效，再取消旧工作；仅当前代次允许提交结果。Manager 串行更新状态，平台调用不占用 UI 线程。停止自动搜索不会解除现有绑定，但禁止后续自动重绑；解绑则同时停止跟踪。

---

# 7. 游戏窗口发现

`IWindowCatalog` 枚举顶层窗口，`WindowMatchService` 根据配置筛选：

```text
EnumWindows → 读取窗口/进程信息 → 过滤候选 → 应用匹配规则
→ 0 个：等待 / 1 个：验证后绑定 / 多个：让用户选择
```

- exe 是自动匹配的主要条件，标题与类名是可选约束；不依赖 `Process.MainWindowHandle` 作为唯一入口。
- 空配置停用自动发现；不得解释为空正则匹配所有窗口。非法正则与超时是配置错误，不能伪装为没有候选。
- 自动候选排除本程序、隐藏、工具与 DWM cloaked 窗口。Owned window 默认排除，诊断选择器可显式显示其排除原因并允许手动选择；硬性无效目标仍不可选。
- 最小化窗口可列出并标记，暂不可用的 Client Geometry 不判成不支持尺寸。
- 多进程或多窗口匹配不得静默选第一个，排序不承担身份决策。
- 进程已退出或读取受限时保留可诊断原因，不使整个枚举失败。

手动选择器显示进程名、标题、PID、窗口标识、状态与失败原因。选择瞬间重新验证身份；列表刷新后过期的候选不能直接绑定。

目标游戏信息暂空时，用户仍可绑定任意有效候选作为手动诊断目标；尺寸匹配不等于已支持该游戏。正式自动化仍受目标兼容性和 Profile 用途限制。

只持久化 exe、标题/类名规则和 Profile 偏好；不持久化 PID/HWND。拖动准星选窗留待后续。

---

# 8. TargetWindowRule 与 GameWindowProfile

窗口匹配配置与游戏布局配置分开。未提供游戏信息时的目标配置：

```json
{
  "schemaVersion": 1,
  "gameId": null,
  "displayName": null,
  "exe": null,
  "titleRule": null,
  "classRule": null,
  "preferredProfileId": "diagnostic-1920x1080"
}
```

Phase 1 内置诊断 Profile：

```json
{
  "schemaVersion": 1,
  "id": "diagnostic-1920x1080",
  "purpose": "DiagnosticOnly",
  "gameId": null,
  "clientSize": { "width": 1920, "height": 1080 },
  "sidebar": { "expandedWidthDip": 360, "collapsedWidthDip": 48 },
  "coordinates": {},
  "regions": {}
}
```

1920×1080 是 Client Area 的 physical pixels，不是 WindowBounds 或 DIP。DiagnosticOnly Profile 永远不授权正式自动化，开发模式也不能绕过输入检查。

正式 Profile 的 ID 包含游戏与布局身份，`purpose=Game`，gameId 必填；正式兼容验证还需覆盖 UI 缩放、语言和布局版本等会改变坐标的条件。这些字段在首次具体游戏适配时补齐，Phase 1 不编造值。

校验结果：`Matched / UnsupportedResolution / GeometryUnavailable / AmbiguousProfile / InvalidProfile`。无支持尺寸仍保持绑定；几何不可用不按 0×0 比较；多匹配先按显式偏好，仍有歧义则让用户选择。尺寸恢复可回到 Ready。未知 schema、重复 ID、非法尺寸或规则返回明确配置错误，不能崩溃或静默使用损坏配置。

---

# 9. 坐标系统与 Geometry 契约

强类型至少区分 `NormalizedPoint`、`ClientPixelPoint/Rect/Size`、`ScreenPixelPoint/Rect`、`CapturePixelPoint/Rect`。Avalonia DIP 仅用于 UI。禁止 Feature 自行处理 DPI 或把裸 Point 传给不同坐标 API。

| Geometry 字段 | 含义 |
| --- | --- |
| WindowBoundsPx | Screen physical pixels 的窗口外框 |
| VisibleFrameBoundsPx | DWM 可见外框，获取失败时回退并标注来源 |
| ClientSizePx | Client 物理像素宽高，Profile 尺寸校验依据 |
| ClientOriginScreenPx | Client 原点在虚拟桌面的物理像素坐标 |
| ClientBoundsScreenPx | Client 在 Screen 坐标中的矩形 |
| MonitorBoundsPx / WorkAreaPx | 显示器范围和工作区，允许负坐标 |
| TargetWindowDpi / Awareness | 目标窗口报告的 DPI 与 awareness |
| GeometryValidity / ObservedAt | 几何有效性与采样时间 |

`GetWindowRect` 可能包含不可见边框且受 DPI virtualization 影响；平台层在验证过的 awareness 上下文读取/转换后才能标注为物理像素。DWM 可见外框和原始外框不得混称。[GetWindowRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect)

Client 原点使用 API 转换，不通过标题栏厚度猜测。矩形右/下边界不包含，宽高为正时才有有效画面。[GetClientRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclientrect)、[ClientToScreen](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clienttoscreen)

`GetDpiForWindow` 依赖目标的 awareness，不保证返回侧边栏所在屏幕的缩放。目标 DPI 与侧边栏自身 RenderScaling 分开，绝不复用一个 DpiScale。[GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)

转换由 CoordinateTransformService / WindowGeometryService 统一提供：

```text
Normalized → ClientPhysicalPixel → Maa Controller 输入坐标
CapturePixel → FrameToClientTransform → ClientPhysicalPixel
ClientPhysicalPixel → ClientToScreen → ScreenPhysicalPixel
Screen 位置与 UI DIP 尺寸分别交给窗口定位适配器
```

规范化点使用 [0,1]，点击映射到 [0,width-1] / [0,height-1]；ROI 使用边界语义，可到 width/height 的排他边界。超范围、NaN、无效尺寸或变换无法确认时拒绝操作，禁止静默截断危险坐标。

每次采样前后核对身份，矛盾几何只做有限重试并标无效。采样不是跨多个 Win32 调用的原子快照；帧与输入还需校验会话代次及几何版本。

---

# 10. Sidebar Window

Sidebar 是独立 Avalonia 顶层窗口，不是游戏 Overlay，Phase 2 交付。默认展开 360 DIP，收起 48 DIP，设置/Profile 字段明确带 Dip 单位。

定位基线选用 `VisibleFrameBoundsPx` 的右边缘、顶部和高度，失败时回退 `WindowBoundsPx` 并显示诊断标记。位置使用屏幕物理坐标，尺寸通过侧边栏自身 RenderScaling 转成 DIP；侧边栏采用无系统边框样式，避免其非 Client 装饰高度造成重复计算。

右侧空间不足时，保持在游戏右侧，不换边、不强行 Clamp。允许部分或完全离屏；主诊断窗口有独立任务栏入口，始终可恢复设置、解绑和展开操作。单屏无边框窗口占满显示器时，侧边栏可能不可见，界面需给出原因。

展开/收起只改变宽度，不解除绑定。跟随定位、恢复显示不激活窗口，不持续抢焦点。V1 默认非全局 Topmost、与游戏进程不建立跨进程 owner 关系，后台时允许被其他窗口遮挡；用户点击侧边栏可以正常激活它。自动化运行中失焦按 §15–18 停止，不由侧边栏跟随逻辑抢回游戏前台。

---

# 11. 窗口跟踪与多显示器

Phase 1 使用 WindowTracker 的串行轮询，默认周期 200ms，允许配置在合理范围内。慢查询时最多一个在途读取，不产生重叠 Timer 回调；未绑定的自动搜索默认每 1 秒枚举一次，绑定后仅查询目标。

读取身份、Geometry、前台、最小化、显示器、DPI，向 GameSessionManager 提交带 BindingGeneration 的结果。仅当前代次可发布；UI 线程接收不可变快照。位置/尺寸/映射信息变化递增 GeometryVersion，时间戳变化本身不递增。

UnsupportedResolution、BoundUnavailable 与已绑定的 Profile 选择状态仍被跟踪。游戏最小化、恢复、跨屏或改变尺寸后重新验证；身份不一致触发 GameLost。停止/退出取消并收束跟踪任务、订阅和查询资源。

Phase 1 验收几何与状态，Phase 2 再验收侧边栏跟随：

- 虚拟桌面可有负坐标，不能 Clamp 到主屏原点。
- 游戏与侧边栏可能位于不同 DPI 显示器，分别处理。
- 自身窗口 RenderScaling 改变后重算 DIP 高度，防止反复乘 DPI。
- 跟随定位不激活游戏，不改变其大小或位置。

WinEventHook 留作后续体验优化，不作为 Phase 1 前置条件。

---

# 12. Capture System 与帧所有权

Phase 3 实现单次 `CaptureAsync`；Phase 6 实现 `StreamAsync`。两者以及自动化中的截图都经过 CaptureCoordinator，同一 Controller 不得被多个入口并发驱动。

`GameFrame` 最小字段：

```text
SessionId / BindingGeneration / GeometryVersion
SequenceId / CapturedAt / MonotonicTimestamp
ClientSizePx / CaptureSizePx
CaptureMethod / FrameToClientTransform / FrameValidity
PixelFormat / Stride / ImageBufferLease
```

Core 的 ImageBufferLease 采用与 UI、Maa、OpenCV 无关的释放契约，不直接持有公开 Mat 或 Avalonia Bitmap。Native buffer 必须复制到自有存储，或明确持有保证有效的租约后才能离开适配器；Vision 内部按需转换成 Mat。

所有权从生产者转交给消费者；分析完成、丢帧、停止、异常及投递失败都释放。预览与分析共享时使用显式复制或受控共享租约，禁止复用仍在被读取的缓冲区。释放必须可追踪并避免重复释放。

截图优先显式启用 Maa 原始尺寸，但还要校验是否完整对应 Client Area。裁剪、缩放和黑边通过 FrameToClientTransform 表达；无法建立可信映射则标为无效，不能用于坐标输入。Maa 的缩放与 raw size 选项以锁定版本为准。[Maa 控制器选项](https://github.com/MaaXYZ/MaaFramework/blob/main/include/MaaFramework/MaaDef.h)

窗口几何或绑定在采集期间改变时，帧作废；分析结果继承帧的版本，过期结果不回写当前 UI、不参与自动化。静止画面并不自动等于旧帧，内容有效性与新鲜度通过测试窗口动态标记和目标游戏实验验证。

---

# 13. 实时分析模型

采用 Latest Frame Wins，Phase 6 实现。明确配置：

```text
CaptureCoordinator
  → Bounded Channel（Capacity=1，FullMode=DropOldest）
  → Vision Processor
  → 带会话/几何版本的 AnalysisResult
```

仅设置 Capacity=1 的默认等待策略不等于丢旧帧。使用 `itemDropped` 或等效所有权逻辑释放被丢弃帧；处理完成、写入失败、停止清空队列时也需释放。[.NET Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)

例如 101 正在处理，102/103 被后来的帧替换，下一张处理 104。在途分析不因每个新帧无限重启；结果发布时再次检查代次及过期策略。

FPS 可配置，Feature 支持 Manual / Periodic / Continuous。一个 Channel 的多 reader 是竞争消费，不能直接当作广播；多个 Feature 扇出需独立有界队列及明确帧共享所有权。

本策略仅适用于实时分析；滚动采集 FrameSequence 必须有序完整保存，不能复用丢帧通道。

---

# 14. MaaFramework 定位与兼容性试验

MaaFramework 是截图与输入基础设施：提供 Win32 Capture、Mouse/Keyboard Input、自动化原语，按需使用 Recognition/Pipeline。不管理 Sidebar、GameSession、Feature 生命周期或长图业务。

默认后台截图候选为 `FramePool | PrintWindow`。Maa 在候选中测试可用性/速度，但 API 可用或较快不等于画面正确。不同程序没有通用截图方案，目标游戏必须验证前台、遮挡、后台情况下的内容和更新情况。[Maa 控制方式](https://github.com/MaaXYZ/MaaFramework/blob/main/docs/en_us/2.4-ControlMethods.md)

Phase 3 同步提供方法切换、单帧预览、实际方法、尺寸和映射诊断。先用受控窗口验证，再在目标游戏可用时记录截图兼容结果；黑帧、旧帧、缺失区域不能仅按 API 成功视为通过。

MaaHost 统一管理日志选项、版本、Native 初始化与 Controller 生命周期。Binding 与 Native 配套、VC++ 前置依赖和加载诊断在首次接入时就验证。[C# Binding](https://github.com/MaaXYZ/MaaFramework.Binding.CSharp)

最小化不调用可能改变窗口状态的截图路径，见 §29。Mac Phase 1 不加载 Maa；后续 Mac 算法使用 FixtureCaptureService。

---

# 15. 自动化输入与运行时检查

正式输入选择 Maa Win32 Seize，普通用户权限，用户主动触发。每次任务开始需要：

1. 当前绑定身份有效，非最小化，Geometry 有效。
2. `purpose=Game` 的 Profile 与已验证目标兼容性通过；诊断 Profile 不可执行真实输入。
3. 输入适配器、Native 中止/释放能力和紧急热键可用。
4. 有限次获取游戏前台，使用 GetForegroundWindow 验证；失败就终止。

每个输入步骤前再次验证前台、身份、Profile、GeometryVersion 及坐标范围；长拖动分段或使用经验证的 Native 中止机制。用户切走、目标移动/尺寸变化、目标退出或状态失效时停止任务，丢弃旧坐标，不持续抢回前台。

Windows 可以拒绝获取前台；启动时验证不是后续每一步的前台保证。[SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)

记录任务实际持有的按键/按钮，仅释放自身输入。目标权限高于本程序时拒绝不受支持的输入，不自动提权、不盲目重试。[SendInput 权限约束](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)

Developer Mode 的 Test Click / Drag 同样遵守这些检查。Phase 4 的测试窗口可使用明确的测试专用 Game Profile 验证输入，但不得把其验证状态迁移到未知游戏。

---

# 16. AutomationCoordinator

统一使用 `AutomationCoordinator` 名称；它在 Application 中协调任务，不由 UI 直接调用 Maa。

```text
Button → ViewModel Command → AutomationCoordinator
→ Automation Task → Game Input Service → Maa Adapter
```

任务具有 CancellationToken、总超时、步骤超时、进度、失败原因和 finally 清理。托管取消不等于 Native 已停止：适配器必须按锁定版本实现并验证停止提交、Native stop、在途操作完成确认与释放顺序。

```text
Idle → Preparing → AcquiringForeground → Running → CleaningUp → Idle
异常/取消 → Cancelling / Failed → CleaningUp
清理成功 → Idle
清理超时或状态无法确认 → Faulted → 显式恢复/重新初始化
```

Cleanup 使用独立的短超时 Token，不继承已经取消的任务 Token。Native 操作仍在执行时禁止销毁其 Controller 或启动新任务；清理失败即使逻辑锁退出，也由 Faulted 状态拒绝后续任务。

UI 和热键消息循环不得被 Native 等待阻塞。普通取消、拖动中止、异常和 Native 无响应分别测试并记录停止延迟；如果所选 Native 实现不能满足停止目标，需更换原语或评估独立进程隔离，不能仅凭 finally 宣称保证恢复。

---

# 17. 自动化互斥与 Capture 调度

同时最多一个自动化任务，V1 对重复启动直接拒绝并显示运行状态，不排队延后抢占鼠标。

```text
取得 AutomationLock
→ CaptureCoordinator 请求暂停实时采集/分析
→ 等待在途截图与分析结束，释放待处理旧帧
→ 检查当前会话并取得前台
→ 自动化独占 Controller 操作通道，按需截图
→ 清理并确认输入已释放、Native 已停止
→ 会话仍有效且此前确实在分析时恢复分析
→ 释放逻辑锁
```

暂停有超时，不能等不到在途操作完成就并发执行输入。紧急停止通道优先于普通任务，不能排在它试图停止的长操作后方；其 Native 调用并发规则按绑定版本验证。

会话丢失、最小化或清理失败时不恢复分析。手动单帧截图也经过同一 CaptureCoordinator，不能绕过自动化独占状态。

---

# 18. 紧急停止

全局紧急停止与 Phase 4 首次输入能力一起交付。默认组合为 `Ctrl + Shift + F10`，用户可配置；注册结果必须可见。

不能使用 F12 作为 RegisterHotKey 默认键，微软将其保留给调试器。[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

热键注册失败或消息循环不可用时，禁用所有真实输入入口并提示修改组合；窗口绑定和截图不受影响。停止操作不依赖 Sidebar 按钮。

```text
禁止新输入 → 取消任务并停止 Native 提交
→ 请求已验证的中止机制 → 确认在途操作退出
→ 释放本任务持有的键/按钮 → 清理或进入 Faulted
```

重复停止应幂等，不能在清理过程中再次启动任务。Phase 4 必须先确定正常路径的停止时限并通过真机验收；Native 无响应路径记录实际边界，未满足时不得开放正式自动化能力。

---

# 19. Visual Processing Layer

通用视觉算法使用 OpenCvSharp。

职责包括：

```text
ROI
Resize
Threshold
Template Matching
Feature Matching
Image Similarity
Image Alignment
Image Stitching
Stability Detection
```

具体业务识别允许采用：

```text
Maa Recognition
OpenCvSharp
OCR
Custom Algorithm
```

Feature 自行组合。

不得要求所有业务视觉算法必须转换成 Maa Pipeline。

---

# 20. ScrollableCaptureEngine

职责分离：

```text
ScrollableCaptureEngine：截图 → 输入拖动 → 等待稳定 → 截图 → 判断是否继续
输出：有序 FrameSequence（含采样时间、会话/几何版本、方向、ROI）

ImageStitcher：Frames → Overlap/Offset → 去重 → Alignment → Merge
输出：StitchedImage + 对齐质量/失败原因
```

采集通过 AutomationCoordinator 与 CaptureCoordinator 运行，拼接不调用输入或窗口系统。采集序列不能用实时分析的 DropOldest 通道，丢失关键帧应显式失败或重采。

帧序列有单一所有者，失败/取消后释放；大序列可按计划落盘，但必须有字节数、帧数、时间和磁盘上限。拼接还限制输出像素/内存，并报告重叠不足等质量问题，不能把错位长图视为成功。

---

# 21. 一维长图支持

V1 支持垂直上/下、水平左/右，不支持二维扫描、横纵组合地图或自由画布。

ScrollCapturePlan 至少包含：

```text
Direction / CaptureRegion / IgnoreRegions
DragStart / DragEnd / WaitForStable
MaxFrames / MaxBytes / MaxOutputPixels
TotalTimeout / StepTimeout / EndDetection
```

终止原因区分：无有效位移、连续重复、达到帧/资源上限、用户取消、超时、窗口/几何失效、Feature 自定义结束和识别失败。达到上限应返回截断状态，不能伪装为内容已全部采完。滚动未发生与画面未刷新需通过稳定性和新鲜度诊断区分。

---

# 22. 画面稳定检测

提供 `WaitForVisualStableAsync`，由 ROI、IgnoreRegions、差异阈值、最小等待、采样间隔、连续稳定样本数和总超时组成。

拖动结束后按间隔采样，连续多个有效样本低于差异阈值才返回 Stable；仅比较两张可能被动态数字或暂时停顿误导。固定 Delay 仅用于最小等待和采样节奏。

失效帧、绑定/几何变化直接失败，不以零差异判稳定。超时返回明确结果由 Feature 决定终止或有限重试，不能无限等待或把超时当稳定。

---

# 23. Feature Module

业务采用 Feature Module，而不是 DLL 插件。

概念：

```csharp
public interface IGameFeature
{
    string Id { get; }
    string DisplayName { get; }

    bool CanRun(GameSessionSnapshot session);

    Task ExecuteAsync(
        FeatureContext context,
        CancellationToken cancellationToken);
}
```

`CanRun` 用于 UI 可用性与预检查，不替代 AutomationCoordinator 在每一步执行前的身份、前台和坐标验证。FeatureContext 提供受调度的服务接口，不暴露 Native Controller 或 HWND。

Feature 可以声明：

```text
NeedsCapture
NeedsForeground
NeedsAutomation
NeedsContinuousAnalysis
SupportedProfiles
```

例如未来：

```text
Features/

  InventoryAnalyzer/
  CharacterAnalyzer/
  MapHelper/
  LongListCapture/
```

新增业务原则上只需要：

```text
Feature
Analyzer
Automation
ViewModel
Optional View
```

不修改窗口系统或 Maa 基础设施。

---

# 24. Developer Mode

诊断能力随基础设施同步交付：

| 阶段 | 能力 |
| --- | --- |
| Phase 1 | 独立主窗口：选窗、绑定/解绑、规则状态、PID/HWND 诊断、Geometry/DPI/Profile、失效原因、复制 JSON |
| Phase 3 | 单帧预览、保存截图、方法切换、实际尺寸/变换/延迟/有效性、Native 加载错误 |
| Phase 4 | Test Click/Drag、任务步骤/耗时、取消/停止状态；所有输入检查仍生效 |
| Phase 5 | 整合诊断工作流、坐标查看器与导出 |
| Phase 6 | Start/Stop Stream、FPS、Sequence、丢帧与资源统计 |

诊断视图标记 Demo/真实模式、数据采样时间、失效数据及兼容性未知状态。生产模式不自动保存所有截图，用户显式导出才落盘；主窗口/任务栏入口不随侧边栏离屏消失。

---

# 25. 坐标调试器

Developer Mode 提供：

```text
Mouse Position

Screen:
x, y

Client:
x, y

Normalized:
x, y
```

提供：

```text
Copy
Copy JSON
```

未来增加：

```text
Pick Point
Pick Region
```

自动生成：

```json
{
  "x": 0.9083,
  "y": 0.7898
}
```

或：

```json
{
  "x": 0.19,
  "y": 0.24,
  "width": 0.51,
  "height": 0.44
}
```

这将作为后续 Feature 开发的重要工具。

---

# 26. macOS M5 开发模式

Mac 不模拟 Win32，也不在启动时构造 Windows 服务。组合根按运行平台或显式 `--demo` 注册替身，界面持续标记 Demo；Windows 平台初始化失败不得静默切为 Fake 冒充成功。

| 接口 | Windows | Mac / Test |
| --- | --- | --- |
| IWindowCatalog | WindowsWindowCatalog | FakeWindowCatalog |
| IWindowGeometryProvider | WindowsWindowGeometryProvider | FakeWindowGeometryProvider |
| IGameCaptureService（Phase 3） | MaaGameCaptureService | FixtureCaptureService |
| IGameInputService（Phase 4） | MaaGameInputService | MockGameInputService |

Phase 1 Fake 场景覆盖出现/消失、多候选、移动、尺寸不支持再恢复、最小化、延迟查询和换绑竞态；它们验证应用逻辑，不证明 Win32 行为。

因此 Mac 可以开发 UI、ViewModel、会话状态机，后续使用真实 Windows Fixture 开发视觉、拼接和 Feature。OpenCvSharp ARM64 Runtime 需通过首次接入实验；M5 本地结果与任意 macOS CI runner 结果分开记录。

---

# 27. Fixture 测试体系

真实 Windows 测试时，应主动积累固定 Fixture。

例如：

```text
fixtures/

  game_home/
  inventory/
  horizontal_scroll/
  vertical_scroll/
  animation/
  unsupported_resolution/
```

每个 Feature 都应尽可能拥有真实截图 Fixture。

算法开发流程：

```text
Windows 获取真实截图
      ↓
保存 Fixture
      ↓
Mac 编写算法
      ↓
Unit / Golden Test
      ↓
CI
      ↓
Windows 真机验证
```

降低 Mac 开发对真实 Windows 游戏环境的依赖。

---

# 28. 普通分析和自动化策略

普通分析允许后台但未最小化的游戏窗口，实际截图兼容性按目标游戏验证。自动化只接受用户主动触发，要求目标前台、正式 Profile、有效会话、可信坐标和可用停止能力。

窗口绑定 Ready、截图成功、输入适配、布局适配分别记录，不用一个 Ready 表示全部成功。目标暂空时只有窗口诊断与测试场景，不宣称已支持某游戏。

自动化结束仅在会话仍有效且原先在分析时恢复分析；不自动恢复被用户主动停止的任务，也不强制把鼠标或焦点移回旧位置。

---

# 29. Minimize 行为

V1 固定策略：

- 游戏非前台但未最小化：保持绑定，后台分析按已验证能力继续。
- 游戏最小化：隐藏侧边栏，暂停 Capture，取消自动化并按 §16–18 清理；不为截图恢复、透明化或修改游戏窗口。
- 游戏恢复：重新验证身份、Geometry、Profile，必要时重建 Capture 资源，丢弃旧帧；恢复侧边栏不抢焦点。
- 主诊断窗口保持可达，最小化期间展示 BoundUnavailable 和最近一次有效数据的失效标记。

Maa 的部分最小化截图实现会采用恢复窗口及透明/点击穿透的伪最小化路径，因此本项目不调用该能力。[Maa 控制方式](https://github.com/MaaXYZ/MaaFramework/blob/main/docs/en_us/2.4-ControlMethods.md)

---

# 30. 设置和本地存储

```text
Application Directory：binaries / builtin profiles / templates
%LocalAppData%/GameSidebar/：settings.json / logs / debug / cache
macOS Demo：该平台的应用数据目录
```

Phase 1 存储目标规则（允许空）、Profile 偏好、诊断配置；后续追加侧边栏 DIP 宽度、停止热键、Capture FPS 与方法。配置带 schemaVersion，原子替换写入；损坏文件保留副本、给出提示并恢复可用默认值，不静默丢弃用户内容。

不持久化 PID、HWND、SessionId 或旧快照用于自动重绑。目标规则和布局 Profile 分开；普通用户无需对程序目录有写权限。数据不上传。

---

# 31. 日志

Phase 1 开始使用 ILogger<T>，包含 Trace / Debug / Information / Warning / Error / Critical。状态日志关联 SessionId、BindingGeneration；Phase 4 起追加 TaskRunId，Capture 日志关联 GeometryVersion / SequenceId。

记录状态转换、绑定/解绑、错误分类、配置与 Native 版本；不要每 200ms 输出一份重复 Geometry。原生 API 错误仅在 API 支持时读取对应错误码，避免记录无意义的旧错误值。

本地滚动日志设置文件大小/保留数量上限，debug/cache 设置容量与清理规则。截图和序列仅显式保存；Maa 自身的录制、错误截图和调试选项也需与产品设置一致。退出时有界刷新日志，不能让日志清理无限阻塞。

---

# 32. CI 架构

Windows x64 与 Mac 分工：

```text
锁定 SDK / runner 镜像系列
→ locked restore（覆盖实际需要的 RID）
→ Release build
→ Core / Application tests
→ 按引入阶段追加 Vision Fixture / Stitching / Native load checks
→ Windows publish win-x64 self-contained
→ 检查 exe、配置、profiles 与 Native 资产
→ ZIP + 测试报告
```

Phase 0–1 不执行尚未存在的 Maa、Vision 或 Stitching 测试。Mac job 构建跨平台项目并运行纯托管测试；Windows 专用集成测试明确标记，缺交互桌面或设备时显示 skipped/pending，不能伪报成功。

CI 发布只生成供验收的 Artifact；是否发布公开 Release 另行操作。测试工具与主程序分包。远端仓库未配置时可先完成工作流文件，但不能宣称 CI 已运行。

本地 Apple Silicon Demo、Windows 自有桌面和目标游戏真机测试记录独立保存。

---

# 33. CI 的职责边界

GitHub Actions 负责发现：

```text
Windows 编译错误
Native dependency packaging error
Unit test failure
Algorithm regression
Publish failure
```

GitHub Hosted Runner 不负责验证：

```text
真实游戏截图
游戏反作弊环境
SetForegroundWindow 实际行为
真实 Mouse Seize
真实窗口 DPI
真实多显示器
游戏更新后的兼容性
```

这些必须在真实 Windows 游戏环境测试。

---

# 34. 发布策略

V1 使用 ZIP 解压运行，不做 MSI/MSIX/Setup/Updater。Phase 1 就提供验收 ZIP，Phase 10 完成发布加固。

```text
Self-contained / win-x64
PublishSingleFile=false
PublishTrimmed=false
PublishAot=false
```

保持 Native DLL 所需目录结构。首次引入 Maa / OpenCV 时分别做 Native 加载、配套版本和打包检查；Maa C# Binding 列有 VC++ 前置依赖，Self-contained 只包含 .NET Runtime，不自动保证所有 Native 依赖齐全。[Binding 依赖说明](https://github.com/MaaXYZ/MaaFramework.Binding.CSharp)

无 .NET SDK/Runtime 的干净 Windows 必须测试解压启动；有前置依赖时给出具体诊断，不能只显示 DLL Load Failure。Phase 1 尚无 Maa/OpenCV，不把它的 ZIP 成功当作后续 Native 发布成功。

---

# 35. 自动更新

V1 不开发自动更新。

更新流程：

```text
GitHub Releases
 ↓
下载新版 ZIP
 ↓
解压运行
```

用户数据位于 `%LocalAppData%`，因此覆盖程序目录不会删除用户配置。

---

# 36. 测试体系

| 层级 | 内容 | 验证边界 |
| --- | --- | --- |
| Core / Application | Profile、配置、状态恢复、窗口身份、代次过滤、坐标、串行轮询与取消 | Mac / Windows 可重复测试，使用可控制时间源 |
| Vision Fixture（后续） | ROI、匹配、稳定性、对齐、拼接、资源上限 | Fixture 算法结果，不代表真实截图兼容 |
| Windows Platform | Interop 编译、可自动化的受控窗口检查、后续 Native 加载/打包 | 需要桌面的测试单列，不默认 hosted runner 满足 |
| Windows Integration | 几何、DPI、多屏、窗口重建、真实游戏截图/输入/停止 | 真实 Windows 桌面与目标游戏 |

Phase 1 必测：空配置、多个候选、候选过期、A 查询迟到覆盖 B、模拟 PID/HWND 复用、尺寸不支持再恢复、最小化、权限不足、坏配置、重复绑解及关闭清理。模拟复用用于确定性验证应用逻辑，不能写成真机已复现。

受控 TestWindow 支持 Client 物理尺寸、普通边框/无边框、多实例、改标题、重建窗口及重启。显示自身实际几何用于对照；测试对象不提供本项目的窗口服务实现。

Phase 3–4 再测试帧映射、正确画面、按键释放、Native 中止、前台丢失、热键冲突及停止延迟。运行结果区分 passed / failed / skipped / pending，保留环境与版本。

---

# 37. 真机测试矩阵

至少记录：

- Windows 11 x64 的实际版本/edition，100% / 125% / 150% DPI。
- Windows 10 22H2 x64 兼容目标的独立结果；无设备时保持待验收。
- 单屏、多屏、混合 DPI、显示器负坐标。
- 普通窗口、无边框窗口、1920×1080 Client 及不支持尺寸。
- 移动、resize、最小化恢复、窗口销毁/重建、进程重启、前台切换。
- Phase 2 起增加侧边栏展开/收起、右侧无空间、完全离屏、主窗口恢复入口和不抢焦点。
- Phase 3 起增加后台遮挡截图、帧内容/尺寸/映射/新鲜度；最小化不截图。
- Phase 4 起增加输入权限、热键注册失败、用户切走、拖动中取消、Native 故障清理。

Phase 1 先做受控窗口工程验收。目标游戏尚未提供时，其兼容性单列 pending；测试窗口通过可进入 Phase 2，但不能勾选真实游戏验收。

---

# 38. 开发阶段与交付门槛

## Phase 0 — Repository Bootstrap

四个生产项目、测试工程、global.json、集中版本与 lock files、Avalonia 主窗口、平台组合根、日志入口、Windows/Mac CI 与验收 ZIP。Mac 用 Demo；Windows 用真实服务，服务未完成时显示能力未就绪。

门槛：工程构建、基础测试与发布链路可运行；远端 CI 未运行时明确记录。

## Phase 1 — Window Binding Diagnostics

WindowCatalog/匹配器、手动选择、GameSessionManager、Geometry/DPI、Profile 校验、串行 Tracker、失效/换绑恢复、诊断 UI、配置、日志、Mac Fake 场景与 Windows TestWindow。

门槛：自动发现机制与手动绑定通过受控窗口测试，正确显示 Client/Window/Monitor Geometry；旧异步结果不污染新绑定，空配置、坏配置和恢复路径可诊断。任务与用例以 `Phase0-1-开发清单.md` 为准。

真实游戏信息可继续为空；工程验收与目标游戏验收分别记录。

## Phase 2 — Sidebar

右侧可见外框定位、展开/收起、无激活跟随、DPI、多显示器、最小化隐藏/恢复与独立主窗口入口。

门槛：跟随和缩放正确；右侧不足不换边，完全离屏仍有恢复操作入口。

## Phase 3 — Capture

Maa 初始化/配套版本、Controller、单帧/后台截图、Fixture、内容预览、方法诊断、FrameToClientTransform、帧所有权和单通道调度。加入最小“用户触发截图→简单分析→显示”的切片验证接口，不等完整 Feature Framework。

门槛：受控窗口截图与资源释放通过；目标可用时验证真实前后台内容与映射，尚不可用则明确 pending，不能推进为已支持游戏。

## Phase 4 — Automation

前台获取与逐步检查、Seize、Click/Drag、独占调度、Native 取消/超时/清理、停止热键与输入诊断。

门槛：先在测试窗口验证停止时限、失焦/窗口变化即停、热键冲突禁用输入和异常释放；真实游戏同样需要单独通过。未证实可停止的原语不开放正式使用。

## Phase 5 — Developer Mode Integration

整合前几阶段已交付的窗口/截图/输入诊断，增加坐标查看、导出和工作流体验；不推迟基础诊断到本阶段才实现。

## Phase 6 — Streaming Analysis

Stream、Capacity=1/DropOldest、丢帧释放、FPS、AnalysisCoordinator、停止清空与失效结果过滤。验收无无限积压、资源泄漏或旧结果串会话。

## Phase 7 — Scroll Capture

纵横一维采集、稳定检测、完整 FrameSequence、帧/字节/时间上限、异常与取消。验收终止原因明确，序列不使用丢帧队列。

## Phase 8 — Stitching

重叠、对齐、去重、纵横拼接、质量评估与输出像素/内存上限。验收真实 Fixture 与不足重叠等失败场景。

## Phase 9 — Feature Framework

根据前期切片归纳 IGameFeature、上下文、注册、UI 入口，迁移 Demo 并验证扩展边界；避免只凭设想提前固定复杂插件体系。

## Phase 10 — Release Hardening

完整干净环境测试、全部 Native 依赖诊断、日志/缓存上限、崩溃恢复、README、最终 ZIP 与真实游戏矩阵。发布前必须补齐目标游戏验收，不因工程阶段完成而跳过。

---

# 39. V1 Framework 最终验收标准

以下是完成条件，不表示当前已通过：

- Windows 11 x64 普通用户运行，Windows 10 22H2 兼容结果独立记录；未通过前不宣称支持该系统。
- 真实目标游戏自动/手动绑定通过，多候选、丢失、重绑和身份检查正确。
- 至少一个真实游戏 1920×1080 Client Profile 通过布局验证，诊断 Profile 不授权输入。
- 侧边栏保持游戏右侧、展开/收起、多屏/DPI、最小化恢复、离屏操作入口正确。
- Maa 单次/后台截图内容正确，映射可信，持续分析有界且帧/结果不过期串会话。
- 前台点击/拖动、权限与逐步检查、Native 取消、热键、异常释放和停止时限通过真机验证。
- 上下/左右滚动序列完整、资源有界，样例拼接通过质量验证。
- Developer Mode、Mac Fixture、CI、干净环境 ZIP、依赖诊断与本地日志可用。
- 无账号、遥测、截图自动上传、自动更新；数据保留与 Native 调试选项符合本地设置。

Phase 1 工程验收可在游戏信息暂空时完成；V1 Framework 最终验收不能省略真实游戏测试。

---

# 40. 主要技术风险与验证时机

| 风险 | 控制与验证 |
| --- | --- |
| 目标游戏未知 | 空配置与手动诊断先落地；目标提供后补真实窗口/截图/输入实验 |
| 窗口复用与异步竞态 | Phase 1 身份核验、代次过滤、失效恢复与确定性竞态测试 |
| DPI / 多屏 / 隐形边框 | Phase 1 明确物理几何，Phase 2 验证侧边栏自身缩放与负坐标 |
| 截图返回成功但内容错误 | Phase 3 预览、动态内容校验、方法实测，不只依赖返回码 |
| 输入失焦/Native 卡死 | Phase 4 逐步检查、分段/中止、独立清理超时、Faulted 门禁 |
| 反作弊与游戏规则 | 目标游戏兼容性不由本架构保证，不绕过限制 |
| 游戏更新/布局改变 | 配置化规则与版本化 Profile，变更后重新验证 |
| 长图动态内容/资源膨胀 | ROI、IgnoreRegions、连续稳定样本、完整序列和资源/质量上限 |
| Native 与架构不匹配 | 首次引入依赖就做两平台加载实验，Windows ZIP 与干净环境验证 |
| 支持平台变化 | 记录实际 OS/SDK/包版本，区分项目兼容与上游支持 |

只记录已有验证证据；尚无目标或设备的项目保持 pending，不用 Demo/CI 成功替代。

---

# 41. 架构原则

- UI 不直接调用 Maa，Feature 不直接调用 Win32，也不保存/解释 HWND。
- GameSessionManager 唯一写状态；快照不可变，异步结果受代次/几何版本约束。
- 业务不自行处理 DPI，不混用 Client、Screen、Capture 和 DIP。
- Ready 不是游戏兼容结论，未知目标和 DiagnosticOnly Profile 不授权正式输入。
- 自动化有界、可请求取消；Native 停止与清理必须验证，未确认结束不复用/销毁资源。
- Capture/Vision、滚动采集/拼接分离；图像所有权明确，实时分析丢旧帧而采集序列保完整。
- 最小化暂停截图和输入；恢复先验证，不为截图修改游戏窗口。
- 平台与 Core 分离；Mac 使用 Fake/Fixture，真实 Windows 行为依靠真机验证。
- 日志、诊断与测试随能力交付，不能全部推迟到发布前。
- 游戏规则与布局进入配置/Profile；Feature 抽象通过实际切片验证后再扩展。

---

# 42. 最终核心数据流

普通实时分析：

```text
Game Window
     ↓
Maa Capture
     ↓
GameFrame
     ↓
Latest Frame Channel
     ↓
Feature Analyzer
     ↓
AnalysisResult
     ↓
ViewModel
     ↓
Sidebar
```

自动化：

```text
Sidebar Button
     ↓
Feature
     ↓
AutomationCoordinator
     ↓
AutomationLock
     ↓
Foreground Game
     ↓
Maa Seize
     ↓
Click / Drag
     ↓
Capture
     ↓
Vision
     ↓
Result
     ↓
Cleanup
     ↓
Sidebar
```

滚动长图：

```text
Feature
   ↓
ScrollableCaptureEngine
   ↓
Capture
   ↓
Drag
   ↓
Wait Stable
   ↓
Capture
   ↓
...
   ↓
FrameSequence
   ↓
ImageStitcher
   ↓
StitchedImage
   ↓
Feature Analyzer
   ↓
Result
```

---

# 43. 最终技术决策摘要

C# / .NET 10 / Avalonia 12 构建应用；Phase 0–1 四项目统一 net10.0，运行时隔离 Windows 与 Demo 实现。后续 Maa 提供截图/前台输入，OpenCvSharp 提供视觉/拼接，具体版本以兼容实验锁定。

GameSessionManager 管理不可变会话快照和代次；TargetWindowRule 负责发现，GameWindowProfile 负责尺寸/布局。游戏信息允许暂空，诊断和正式目标兼容性分别管理。

侧边栏贴可见外框右侧，不换边、不 Clamp、不自动抢焦点；独立主窗口确保离屏后仍可操作。输入要求前台、可信会话/坐标及已验证的停止机制；最小化暂停截图和输入。

程序维持普通用户权限、Local-first、Self-contained ZIP、无账号/遥测/自动上传/自动更新的边界。Windows CI 验证构建与打包，真实 Windows 和目标游戏验证最终行为。

---

# 44. V1 之后的扩展方向

在底层框架稳定后，可以按实际需要增加：

```text
更多 GameWindowProfile
OCR
模板库
实时 HUD 数据分析
更复杂 Feature
Replay Regression Test
Feature-specific State Machine
二维 Scroll Capture
插件系统
自定义 Profile Editor
性能监控
GitHub Release 自动打包
```

其中任何一项都不应要求重构现有：

```text
GameSession
Capture
Automation
Window Tracking
Coordinate
Feature
```

基础架构。

---

# 结论

Phase 1 先完成可诊断的窗口绑定；V1 Framework 再逐步构建完整的游戏视觉辅助运行时。

首先解决：

```text
找到游戏
→ 正确绑定
→ 正确跟随
→ 正确截图
→ 正确处理坐标
→ 安全自动化
→ 可取消
→ 可调试
→ 可测试
```

随后再实现：

```text
长图
→ 分析
→ Feature
→ 具体游戏功能
```

只要这一基础框架稳定，后续业务功能就可以主要集中在：

```text
ROI
Recognition
Automation Task
Feature UI
```

四个区域，而不再反复处理 Win32、DPI、窗口、鼠标接管和 MaaFramework 生命周期问题。

本 V1.1 修订版作为后续开发基线。工程实现与各阶段验收另行记录。

---

# 修订记录

## V1.1 — 2026-09-29

| 评审项 | 已合并的规范章节 | 落地阶段 |
| --- | --- | --- |
| R01 阶段范围 | §2、§38、§39 | Phase 0–1 / V1 验收 |
| R02 身份、代次与恢复 | §6、§11、§36 | Phase 1 |
| R03 坐标与 DPI | §9–11 | Phase 1–2 |
| R04 发现、多候选与 Profile | §7、§8 | Phase 1 |
| R05 项目与 Mac 替身 | §3、§5、§26 | Phase 0–1 |
| R06 截图/输入坐标契约 | §9、§12、§15 | Phase 3–4 |
| R07 Native 取消与清理 | §15–17 | Phase 4 |
| R08 全局停止热键 | §18 | Phase 4 |
| R09 所有权与调度 | §12、§13、§17、§20、§21 | Phase 3–8 |
| R10 最小化策略 | §14、§29 | Phase 1 状态 / Phase 3–4 行为 |
| R11 侧边栏可达性 | §10、§11 | Phase 2 |
| R12 诊断与实验前移 | §24、§32、§34、§38 | 随对应能力交付 |

另补充 SDK/传递依赖锁定、Windows 10 兼容定位、Native 配套验证、基础日志与存储恢复。链接为评审时使用的官方依据；实施时以锁定版本与真实测试为准。
