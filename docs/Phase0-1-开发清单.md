# Phase 0–1 开发清单（V1.1 对齐版）

编制/修订日期：2026-09-29。规范依据：`Windows游戏侧边栏视觉辅助软件-总体技术规划书-v1.0.md` 正文 V1.1，重点为 §2–11、§26、§30–38。文件名中的 v1.0 为历史名称；评审文档仅保留问题由来，执行以修订计划书为准。

**交付目标：一个可在 Windows x64 上运行的窗口绑定诊断程序。** 用户可以枚举窗口、手动绑定、查看持续更新的 Geometry / Profile / 状态；配置游戏规则后可以自动发现。Mac 能运行同一 UI 的明确标识的 Demo 模式并测试应用逻辑。

当前已开始工程实施；详细验证结果与未完成的 Windows 验收见 `docs/testing/phase1-acceptance.md`。清单保留 Phase 0 前置任务，Phase 1 任务编号为 P1-01–P1-11。

## 1. 范围与完成标准

### 本阶段包含

- Solution、依赖管理、基础日志、构建和测试流水线。
- 自动发现机制、窗口选择器、绑定/解绑和多候选处理。
- 唯一会话写入者、不可变状态快照、过期异步结果过滤。
- Client / Window / Monitor Geometry 及 DPI 诊断数据。
- JSON Profile 加载、尺寸校验、不支持尺寸和恢复。
- 默认 200ms 的串行窗口跟踪循环、失效恢复和应用退出清理。
- Mac Demo 场景、Windows 受控测试窗口、验收记录模板。
- 可供验收的 self-contained win-x64 ZIP。

### 本阶段不纳入交付

右侧侧边栏及跟随、Maa、截图、OpenCvSharp、OCR、鼠标/键盘自动化、全局停止热键、长图、完整 Feature 注册系统。它们的规范已写入计划书对应章节，本阶段不提前创建空实现。

### 游戏信息暂空的处理

| 信息 | 当前值 | 处理方式 |
| --- | --- | --- |
| gameId / 显示名称 | 未配置 | 保持 null，不编造真实游戏信息 |
| exe / titleRule / classRule | 未配置 | 不进行自动搜索；显示“未配置目标，可手动选择窗口” |
| 初始布局尺寸 | 1920×1080 Client physical pixels | 提供 diagnostic-only Profile，不宣称验证过游戏布局 |
| Windows 验收设备 | 待确认 | 工程可先开发；真机用例必须标明待验收 |
| Windows 10 22H2 | V1.1 兼容目标 | 单独记录兼容性验证，不等同于 .NET 官方支持承诺 |

自动发现通过测试程序的明确规则验证；实际游戏规则之后填写。未知窗口可以手动诊断，UI 分别显示“窗口绑定有效”“尺寸匹配”“目标游戏未验证”。

**完成状态分开记录：**

- **工程交付：** 编译、单元测试、Windows 受控窗口绑定测试通过。
- **目标游戏验收：** 等目标可提供后完成；未执行时不能声称原规划“稳定找到真实游戏”的验收已经通过。

## 2. 最小工程结构

```text
GameSidebar.sln
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
.gitignore
.github/workflows/build.yml

src/
  GameSidebar.Core/
    Sessions/
    Geometry/
    Profiles/
  GameSidebar.Application/
    Abstractions/
    Discovery/
    Sessions/
  GameSidebar.Platform.Windows/
    Interop/
    Discovery/
    Geometry/
  GameSidebar.App/
    Views/
    ViewModels/
    Composition/
    Development/
    Storage/

tests/
  GameSidebar.Core.Tests/
  GameSidebar.Application.Tests/
  GameSidebar.Platform.Windows.Tests/

tools/
  GameSidebar.TestWindow/

assets/profiles/diagnostic-1920x1080.json
docs/testing/phase1-acceptance.md
```

生产代码初期只建四个项目；测试窗口是验收工具，不随正式应用默认发布。上面为预期结构，并非已经创建的文件。

依赖约定：Core 无 Avalonia / Win32 / Maa / OpenCV；Application → Core；Platform.Windows → Application / Core；App 的 Composition Root 负责注册 Windows 或 Fake 实现。Views / ViewModels 只调用应用接口，不直接 P/Invoke。

按计划书 §3，四个生产项目统一使用 `net10.0`，Windows 实现标注平台限制，在 Composition Root 使用运行平台判断；Mac 不构造 Windows 服务。RID 仅在对应还原/发布步骤指定，不全局强制 win-x64。若未来改用 Windows 专用 TFM，先修订项目/启动入口与构建矩阵，不在实施中隐式改变本基线。

## 3. 已确定的数据与接口契约

### 数据模型

| 模型 | 最小内容与语义 |
| --- | --- |
| WindowId | 运行时不透明标识；Native HWND 仅由平台实现解析，不能持久化或由业务操作 |
| WindowCandidate | ID、PID、可用的进程身份、标题、类名、状态、筛选/访问失败原因 |
| WindowIdentity | 窗口标识、PID、可获取的进程创建时间与核验结果；信息不足可诊断，但不能进入 Ready |
| WindowGeometrySnapshot | 物理坐标外框/可见外框、Client 尺寸与 Screen 原点、显示器、DPI 信息、采样时间与有效性 |
| GameSessionSnapshot | SessionId、BindingGeneration、GeometryVersion、身份、几何、ProfileValidation、State、SelectionKind、BindingMode、AutoDiscoveryEnabled、TargetCompatibility、最小化/前台、ObservedAt、Error |
| GameWindowProfile | schemaVersion、id、purpose、gameId、clientSize、带 Dip 单位的宽度；DiagnosticOnly 可空 gameId，Game 必填；坐标和区域先留空 |
| ProfileValidationResult | Matched / UnsupportedResolution / GeometryUnavailable / AmbiguousProfile / InvalidProfile，以及原因 |
| WindowOperationError | 分类错误码、可读说明、可选 Native 错误码；错误不是“空窗口列表”的同义词 |

`WindowId` 的具体编码由平台适配器决定，不需要为它搭建通用句柄框架。关键是业务不解释它、每次重新验证、快照不跨会话复用。

### 接口职责

| 接口/服务 | 职责与操作 |
| --- | --- |
| IWindowCatalog | `EnumerateAsync` 获取候选；原始平台信息不泄漏为 UI 控件类型 |
| IWindowGeometryProvider | 对指定身份 `ReadAsync`，同时验证有效性并返回几何/运行状态 |
| IProfileRepository | 从配置加载并验证 Profile；不负责窗口发现 |
| WindowMatchService | 用有效规则筛选候选，返回零个/一个/多个结果 |
| GameSessionManager | `StartDiscoveryAsync`、`BindAsync`、`UnbindAsync`、`StopDiscoveryAsync`、当前快照与更新通知 |
| WindowTracker | 单实例串行周期读取，向 Manager 提交带代次的结果 |

I/O 与长任务接收 CancellationToken，结果区分正常空值、失效、权限不足、取消和错误。UI 调度留在 App。用可替换时间源验证轮询和状态转换，测试不依赖真实等待。

### 状态和恢复

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

`Ready` 仅代表身份、几何和尺寸有效，`TargetCompatibility=Unknown` 的诊断目标也可显示 Ready，但绝不授权输入。BindingMode 为 Auto/Manual；NeedsSelection 使用 SelectionKind 区分窗口选择与保留现有绑定的 Profile 选择。

- SessionId 在接受新绑定时生成；旧窗口消失后的新绑定必须新建 SessionId。
- BindingGeneration 在 Manager 生命周期内单调递增；换绑、解绑、失效或停止旧工作时先递增，再取消旧操作，只接受当前代次结果。
- GeometryVersion 在位置、尺寸、显示器或映射相关 DPI 改变时递增；采样时间改变不递增。
- 身份信息不足/暂时查询失败进入 BoundUnavailable，确认身份不一致进入 GameLost；旧数据可展示但必须标失效。
- 停止自动搜索将 AutoDiscoveryEnabled 置为 false，保持现有绑定与跟踪，但禁止窗口丢失后的自动重绑；解绑同时停止跟踪。
- UnsupportedResolution、BoundUnavailable 和已绑定的 Profile 选择继续跟踪；配置修复或几何恢复后重新验证。

Normalized 与 Capture 坐标的完整实现留到后续阶段；Phase 1 落实 Client/Screen/Size 和 Geometry 数据契约，避免为了占位实现不使用的变换。

## 4. 有依赖关系的开发任务

任务按可独立检查的提交拆分。检查框只在实现并满足完成条件后勾选。每项都必须给出实际文件/测试或验收记录；测试环境缺失标为 pending，不用文档已完成代替工程已完成。

### P0-01｜建立工程与依赖基线

依赖：无。规范：计划书 §3–5。

- [x] 初始化本地 Git，补 `.gitignore`、`.editorconfig`；远端仓库配置作为 CI 执行前置项。
- [x] 创建上述四个生产项目和首期测试项目，整理引用方向。
- [x] 用 `global.json` 锁定可用的 .NET 10 SDK；`Directory.Packages.props` 固定所有直接包版本。
- [x] 开启 nullable、平台兼容分析和 CI 警告检查；启用并提交 NuGet lock files，CI 使用 locked restore。
- [x] 记录 SDK / Avalonia / MVVM / DI / 日志 / 测试框架的具体版本与选择结果。此阶段不引入 Maa 和 OpenCV。

完成条件：Mac 与 Windows 构建路径明确；Core 无平台依赖；普通开发无需 Windows RID。

### P0-02｜可启动的 Avalonia 主窗口及组合根

依赖：P0-01。

- [ ] 创建主窗口，显示程序版本、当前模式和状态区域。
- [ ] Windows 注册真实平台服务入口，未实现时明确显示能力未就绪；macOS 注册开发替身，允许 Windows 显式启动 `--demo`。
- [ ] 为 Demo 持续显示明显标识；Windows 服务失败不能静默切换 Demo 冒充成功。
- [ ] 接入 ILogger 和应用生命周期，关闭时能够通知后台服务停止。

完成条件：Mac 能打开 Demo UI；Windows 能打开真实模式 UI；错误信息可见且不导致无限初始化循环。

### P0-03｜最小 CI 与 Windows 验收产物

依赖：P0-01、P0-02。远端未配置时先交付工作流文件并标注“未运行”。

- [ ] Windows 固定 runner 镜像大版本，安装锁定 SDK，执行 restore / Release build / tests。
- [ ] 添加 Mac 构建与纯托管测试任务；Apple Silicon 上的本地 Demo 运行另行记录，不将任意 Mac runner 当作 M5 真机。
- [ ] `dotnet publish` 指定 App、`-r win-x64 --self-contained true`；首次 restore 包括发布所需 RID，后续使用锁定结果。
- [ ] 设置 `PublishSingleFile=false`、`PublishTrimmed=false`、`PublishAot=false`，降低初期打包变量。
- [ ] 上传 ZIP 与测试结果；Phase 0 检查 exe 与基础配置，P1-02 后增加 profiles 完整性检查。测试窗口另包，不混入应用发布目录。
- [ ] CI 的无桌面检查与真实桌面启动测试分开，不能用“发布成功”替代“UI 可运行”。

完成条件：已配置远端时提供成功运行记录与 ZIP；未配置时明确缺少的验证，不填写“CI 已通过”。

### P1-01｜冻结会话、坐标和错误模型

依赖：P0-01。规范：计划书 §6、§8、§9。

- [ ] 按第 3 节实现最小模型；区分 ScreenPixel / ClientPixel / Size，允许负 Screen 坐标。
- [ ] 明确矩形右/下边界不包含，尺寸不能为负；没有有效几何时不填入伪造的 0×0 Profile 结果。
- [ ] 实现第 3 节 SessionId / BindingGeneration / GeometryVersion 更新规则，以及 SelectionKind、BindingMode、AutoDiscoveryEnabled 和 TargetCompatibility。
- [ ] 定义 API 失败、身份不足、窗口销毁、权限不足、无配置、配置无效和无候选的不同结果；身份不足不能 Ready，确认身份不一致才按 GameLost 处理。

完成条件：代码层不能无意混用 Screen 与 Client 坐标；状态模型能够表达所有恢复路径。

### P1-02｜Profile 与匹配配置

依赖：P1-01。规范：计划书 §7、§8。

- [ ] 分开定义 TargetWindowRule 与 GameWindowProfile。
- [ ] 使用计划书 §8 的配置结构创建 `diagnostic-1920x1080` Profile，`purpose=DiagnosticOnly`、gameId 为 null、坐标和区域为空；侧边栏宽度字段明确为 Dip。
- [ ] 目标游戏名称、exe、titleRule、classRule 默认留空；空配置停用自动发现。
- [ ] 校验 schemaVersion、唯一 ID、正数尺寸、用途及可选规则；正则有超时，非法表达式给出错误。
- [ ] 多个 Profile 匹配时按显式偏好选择，仍有歧义返回 AmbiguousProfile，进入 NeedsSelection（Profile）且保留跟踪；无效 schema/重复 ID/损坏 JSON 进入可恢复的配置错误。
- [ ] 手动诊断可独立于正式目标配置运行；正式游戏布局兼容性保持 Unknown；验证 Game Profile 的 gameId 必填，DiagnosticOnly 不授权正式输入。

完成条件：尺寸匹配与目标身份是两项独立判断；缺配置、坏配置、不支持尺寸均可解释。

### P1-03｜Windows 窗口枚举和匹配

依赖：P1-01、P1-02。

- [ ] 封装最小 Win32 Interop：EnumWindows、进程归属、窗口标题/类名、可见性等 API。
- [ ] 读取进程信息使用有限权限；捕获退出/访问失败，不把整个候选列表查询一起打断。
- [ ] 自动候选排除本程序、隐藏/工具/cloaked/owned 窗口；诊断选择器可显式显示 owned window 与排除原因并允许手动选择，硬性无效目标仍不可选。
- [ ] 默认枚举包含可识别的最小化主窗口并标记状态；几何不可用不冒充正常值。
- [ ] exe 匹配大小写规则固定，标题和类名可选；不依赖 Process.MainWindowHandle 作为唯一发现方法。
- [ ] 无候选返回等待状态；多个候选返回 NeedsSelection，不静默选择“第一个”。

完成条件：相同 exe 的多窗口可区分；进程读取失败可诊断；刷新列表不会让 UI 卡住。

### P1-04｜Windows Geometry / DPI 读取

依赖：P1-01。规范：计划书 §9、§11。

- [ ] 在读取前确认程序/线程 DPI awareness；在 Windows 诊断输出中展示，不盲目叠加冲突的 DPI 初始化调用。
- [ ] 按 §9 字段名实现 WindowBoundsPx、VisibleFrameBoundsPx、ClientSizePx、ClientOriginScreenPx、ClientBoundsScreenPx；Client 原点用 API 转换。
- [ ] 获取 MonitorFromWindow 对应 MonitorBoundsPx / WorkAreaPx，保留负坐标；展示 TargetWindowDpi / Awareness、GeometryValidity / ObservedAt。
- [ ] 区分调用方 awareness 与目标 awareness，验证非 PerMonitor 目标的诊断行为；Phase 1 不引入侧边栏布局或用目标 DPI 推算 UI 尺寸。
- [ ] API 返回失败时读取适用的 Native 错误信息；DWM 获取失败有明确回退标记。
- [ ] 读取前后核对身份；持续拖动/resize 时允许非原子采样，但对矛盾或无效数据最多有限次重试，不发布伪一致快照。
- [ ] 定义 GeometryVersion 对位置、尺寸、显示器/DPI 等映射相关变化递增；普通时间戳变化不递增。

完成条件：测试窗口的 Client physical size 与诊断一致；100% / 125% / 150% 下不重复乘缩放；读取失败不会触发错误的 Profile 匹配。

### P1-05｜GameSessionManager 与绑定生命周期

依赖：P1-02、P1-03、P1-04。规范：计划书 §6、§7、§11。

- [ ] 实现自动发现、手动绑定、解绑、停止搜索、重新搜索与重试；停止搜索保留当前跟踪，解绑才停止跟踪，二者均禁止意外自动重绑。
- [ ] 选择瞬间再验证候选身份，处理列表显示后窗口已关闭的情况。
- [ ] 每次换绑/解绑使旧 BindingGeneration 失效，取消旧工作；使用串行状态更新避免数据竞争。
- [ ] 只由 Manager 发布不可变快照；订阅者异常不能结束后台跟踪。
- [ ] 实现第 3 节状态表；区分手动目标丢失和自动目标丢失的重绑策略。
- [ ] 保留最近一次错误原因；暂时身份不足/几何失败进入 BoundUnavailable，身份不一致进入 GameLost，无效配置进入 Faulted（InvalidConfiguration）；恢复后重新验证。

完成条件：快速从 A 切换 B 后，A 的延迟结果不会改变 B；手动解绑后不会自动绑回来；窗口重启建立新 SessionId。

### P1-06｜WindowTracker 与动态重校验

依赖：P1-05。

- [ ] 默认 200ms 串行轮询，不使用会重叠触发的异步 Timer 回调；慢查询时最多一个在途读取。
- [ ] 已绑定时只查询目标；未绑定自动搜索默认每 1 秒，不每 200ms 全量枚举进程；Profile 待选择状态继续跟踪现有目标。
- [ ] 更新前台、最小化、Geometry、Monitor、DPI、Profile 状态。
- [ ] 尺寸变化重新校验 Profile；不支持→支持、身份信息暂缺→恢复均重新验证；最小化不判 0×0 不支持、不主动恢复窗口，恢复后重读几何。
- [ ] 发现窗口身份失效后立即使当前代次不可用，再按绑定模式恢复。
- [ ] 应用关闭取消并等待跟踪任务，释放订阅、定时器与进程查询资源。

完成条件：持续运行只有一个跟踪循环；关闭后没有继续回调 UI；生命周期变化通过自动测试覆盖。

### P1-07｜窗口选择与实时诊断 UI

依赖：P0-02、P1-05、P1-06。

- [ ] 候选列表显示标题、进程名、PID、窗口标识、最小化及可选择状态。
- [ ] 支持刷新、选择、绑定、解绑、开始/停止自动搜索；取消选择不改变当前绑定。
- [ ] 未配置目标时可进入手动选窗，显示具体下一步，不显示无限“正在寻找游戏”。
- [ ] 按 §9 命名展示物理像素 Geometry、Client Screen 原点、Monitor、DPI/awareness、Profile、前台/最小化、状态和兼容性；DWM 回退来源可见。
- [ ] 显示采样时间和数据有效性；旧数据可以保留供诊断，但必须标为失效。
- [ ] 提供复制诊断 JSON；包含版本、模式、会话代次、几何与错误，不持久化 HWND 作为下次绑定依据。
- [ ] 所有 UI 属性更新切到 UI 线程；刷新和重复点击有执行中状态；窗口/Profile 两种待选择给出各自操作，不把已绑定目标清空。

完成条件：仅使用此界面即可完成“选窗→绑定→移动/resize→诊断→解绑”，不依赖调试器。

### P1-08｜基础设置与本地日志

依赖：P0-02、P1-02、P1-05。

- [ ] Windows 写入 `%LocalAppData%/GameSidebar/`；Mac Demo 使用其应用数据目录。
- [ ] 仅持久化目标匹配规则、Profile 偏好和诊断设置，不持久化 PID/HWND/会话快照作为绑定依据。
- [ ] 设置文件带 schemaVersion、原子替换写入；损坏文件保留副本并提供恢复默认提示。
- [ ] 记录 SessionId / BindingGeneration、状态转换及错误；普通日志不每 200ms 输出一份完整几何。
- [ ] 为日志设置保留上限，记录应用与 OS/架构版本，便于远程定位问题。

完成条件：重启可恢复有效设置；设置损坏可启动；没有管理员写入权限要求。

### P1-09｜Mac Demo 与状态回归

依赖：P1-01、P1-05、P1-06、P1-07。

- [ ] Fake 服务支持无窗口、单窗口、多候选、移动、尺寸变化、最小化/恢复、消失和重启场景。
- [ ] 增加 A 查询延迟、随后绑定 B、A 迟到返回的竞态场景。
- [ ] 使用可控制时间源，验证轮询不重入、取消与订阅清理，不以随机 sleep 验证时序。
- [ ] Core 测试覆盖 Profile schema/尺寸、空配置、坏规则、负 Screen 坐标及几何有效性。
- [ ] Application 测试覆盖自动/手动绑定、Profile 歧义、身份不足、停止搜索保留绑定及全部恢复路径；查询错误不伪装成空候选。

完成条件：Mac 和 Windows CI 均能重复运行同一套逻辑测试；Demo 成功不计为 Windows API 成功。

### P1-10｜受控 Windows 测试窗口与平台检查

依赖：P1-03、P1-04；完整绑定验收另依赖 P1-07。

- [ ] 创建最小测试程序：显示自身实际 Client 像素尺寸与位置，支持固定 Client 尺寸、普通边框/无边框切换。
- [ ] 支持两实例、改标题、关闭重开；验收记录清楚区分进程重启和同进程窗口重建。
- [ ] 支持 1920×1080 与一个不支持尺寸，不用“外框尺寸=1920×1080”代替 Client 尺寸。
- [ ] Windows 单元/平台检查与需要交互桌面的集成检查分组；未执行的桌面测试标为跳过/待验收，不伪报通过。
- [ ] 准备多 DPI、负坐标显示器、权限读取失败等手工步骤。

完成条件：有可重复控制的验证对象，不需要先提供目标游戏名称；测试工具不会随主程序自动运行。

### P1-11｜验收、打包与交接

依赖：P0-03、P1-07、P1-08、P1-09、P1-10。

- [ ] 执行第 5 节矩阵，记录 OS 版本、架构、缩放、显示器拓扑、程序版本与失败原因。
- [ ] Windows 无 .NET SDK 环境解压 ZIP，启动并完成手动绑定；有 SDK 的开发机运行不能替代此项。
- [ ] 核对 Windows 10 22H2 的兼容验证结果；无设备则保留待验收，不擅自移除约定的兼容目标。
- [ ] 补 README：Mac Demo、Windows 运行、配置字段、日志位置、已知限制。
- [ ] 给出工程交付结论、待验收清单和目标游戏配置模板；游戏字段继续允许空值。

完成条件：产物、测试结果和未验证项一起交付；不将 Phase 1 宣称为可截图/可自动化的完整侧边栏。

## 5. 验收矩阵

以下时间是本清单的**验收目标**，不是已经测得的性能。以普通负载、有效 API 响应的测试环境为前提；慢查询必须可诊断，不能靠并发增加积压。

| 编号 | 场景 | 期望结果 | 验证环境 |
| --- | --- | --- | --- |
| A01 | 初次启动，目标配置全空 | 显示未配置，手动选择可用，没有默认全匹配搜索 | Mac Demo + Windows |
| A02 | 自动规则匹配 0 / 1 / 2 个候选 | 分别等待 / 绑定 / 请求选择；结果顺序不影响决策 | 自动测试 + 测试窗口 |
| A03 | 列表选中后目标关闭 | 绑定失败有原因，可刷新重选，不崩溃 | Windows |
| A04 | Client=1920×1080，有标题栏 | Client 正确匹配；外框更大不影响匹配 | Windows |
| A05 | 改成不支持尺寸，再改回 | 保持绑定，UnsupportedResolution 与 Ready 正确恢复 | Demo + Windows |
| A06 | 在同屏移动/改变大小 | 正常负载下连续 20 次操作至少 19 次在 500ms 内更新；记录耗时与超限原因，相关 GeometryVersion 正确变化 | Windows |
| A07 | 100% / 125% / 150% DPI | Client 物理尺寸正确，没有按 DIP 再缩放一次 | Windows 真机 |
| A08 | 移到不同 DPI 显示器及负坐标区域 | 位置、显示器信息正确；游戏 DPI 与 UI 缩放未混用 | Windows 多屏 |
| A09 | 最小化后恢复 | 保持身份，几何不可用可解释，恢复后重新验证 | Windows |
| A10 | 前台切换到其他应用 | 前台标记变化，程序不主动激活游戏 | Windows |
| A11 | 目标关闭重启/窗口重建 | 旧会话失效；自动模式按规则重新绑定，手动模式等待用户 | Demo + Windows |
| A12 | A 读取很慢时切换绑定 B | A 的迟到结果被丢弃，UI 始终属于 B | 可控异步单元测试 |
| A13 | PID/HWND 复用的模拟场景 | 身份不一致使旧会话失效，不仅依赖 IsWindow | Fake 平台契约测试 |
| A14 | 手动解绑 | 当前跟踪停止，无意外自动重绑，可显式重新开始 | Demo + Windows |
| A15 | 无权限读某进程/原生查询失败 | 错误可解释，其他候选不受影响，旧信息标为失效 | Fake + 可复现 Windows 场景 |
| A16 | 损坏设置、无效 Profile、正则超时 | 错误有区分，可恢复，不持续阻塞 UI | 自动测试 |
| A17 | 重复绑定/解绑 50 次后关闭 | 没有重复跟踪循环、悬挂进程或持续增长的资源趋势 | Windows |
| A18 | 连续运行 30 分钟 | 响应正常、日志有界、资源无持续增长趋势；记录观测值 | Windows |
| A19 | 无 SDK 的 Windows 解压运行 | 可启动、可读取 profiles、可绑定测试窗口 | Windows 干净环境 |
| A20 | 真正目标游戏 | 自动/手动绑定、几何、尺寸与重启恢复通过 | 待目标信息与真机 |
| A21 | 多个 Profile 同尺寸、偏好缺失 | NeedsSelection（Profile），保留跟踪；选择后重新验证，不静默取第一个 | 自动测试 + Demo |
| A22 | 已自动绑定时停止搜索，再关闭目标 | 停止搜索后继续跟踪当前目标；关闭后不自动重绑 | 自动测试 + Windows |
| A23 | 进程创建信息暂不可读，之后恢复 | 身份未充分验证时 BoundUnavailable；数据恢复后重验，不用缓存冒充有效身份 | Fake 平台契约测试 |
| A24 | DWM 可见外框读取失败 | 回退 WindowBoundsPx 且标明来源，不影响有效 Client 尺寸校验 | Fake + 可复现 Windows 场景 |

A07–A09 只验证数据读取和状态，侧边栏的跨屏跟随验收仍属于 Phase 2。A13 的确定性模拟用于验证逻辑；不能声称已经在真机强制复现系统 HWND 复用。

## 6. 推荐实施顺序及工作量

| 批次 | 任务 | 完成后可观察的结果 |
| --- | --- | --- |
| 1 | P0-01 → P0-02 → P0-03 | 跨平台壳程序、版本基线、构建产物 |
| 2 | P1-01 → P1-02 | 稳定的数据与配置契约 |
| 3 | P1-03、P1-04、P1-10 的基础部分 | Windows 能列窗、读几何，有可控验证对象 |
| 4 | P1-05 → P1-06 → P1-07 | 完整手动/自动绑定与实时诊断 |
| 5 | P1-08、P1-09、P1-10 的剩余部分 | 状态回归、配置恢复、真机用例 |
| 6 | P1-11 | 验收 ZIP、记录、待验证事项 |

### 规范、任务与验收用例对应

| 任务 | 计划书 V1.1 章节 | 主要证据/用例 |
| --- | --- | --- |
| P0-01 | §3–5 | SDK/依赖锁定、引用与平台构建 |
| P0-02 | §5、§26 | A01，真实/Demo 模式显式区分 |
| P0-03 | §32–34 | 构建报告、ZIP；最终结合 A19 |
| P1-01 | §6、§8、§9 | A12、A13、A23，模型与版本规则 |
| P1-02 | §7、§8 | A01、A05、A16、A21 |
| P1-03 | §7 | A02、A03、A15 |
| P1-04 | §9、§11 | A04、A06–A09、A24 |
| P1-05 | §6、§7 | A03、A11–A14、A21–A23 |
| P1-06 | §6、§11、§29 | A05–A11、A17、A18、A22 |
| P1-07 | §7、§24 | 手动完整流程及 A01、A15、A21、A24 |
| P1-08 | §30、§31 | A16、A18，本地设置与日志 |
| P1-09 | §26、§36 | Demo 与确定性状态/竞态回归 |
| P1-10 | §36、§37 | 受控窗口、平台数据与独立环境记录 |
| P1-11 | §34、§37、§38 | A01–A19、A21–A24 的结果；A20 单列 pending |

对熟悉 C# 桌面开发、可以使用 Windows 测试机的一名开发者，粗估 **8–13 个有效工作日**，包括基础工程、窗口服务、诊断 UI、回归和平台修正。这个区间是排期参考，不是承诺；不含等待设备、目标游戏适配或难以复现的 DPI 兼容问题。应在首个 Windows 几何验证完成后重新估算。

## 7. Phase 1 交接门槛

- [ ] 工程能够在 Mac 开发，在 Windows x64 发布。
- [ ] 手动绑定与配置后的自动发现机制可用，空配置行为明确。
- [ ] 快照坐标、身份、代次、错误与恢复规则均已实现并验证。
- [ ] 支持尺寸和不支持尺寸都可诊断，最小化与失效不冒充有效画面。
- [ ] WindowTracker 串行、可取消、可停止，没有重复后台循环。
- [ ] 受控 Windows 窗口测试通过，测试记录与产物可复现。
- [ ] 游戏名称和匹配规则可继续留空；A20 明确列为待验收。

满足工程门槛后可以进入 Phase 2。按计划书 V1.1，“真实游戏窗口绑定通过”仍需在目标确定后补齐；设备缺失导致未执行的工程用例同样保持 pending，不能据此宣称工程验收全部完成。

## 8. 本次修订记录

- 以已修订计划书 V1.1 代替评审建议作为执行规范，保留原任务编号。
- 固定四项目 net10.0、默认轮询/搜索周期和 DiagnosticOnly 配置，不再将其留为实施时猜测。
- 对齐状态图、身份不足、Profile 歧义、停止搜索与解绑的不同语义。
- 补齐可见外框回退、目标与调用方 DPI awareness、所有快照字段及版本更新规则。
- 将 14 个任务关联到规范章节，验收从 20 项补充到 24 项；真实游戏信息继续留空。
- 工程检查框保持未完成；本次只修改文档，没有构建代码或执行真机测试。

## 9. 工程实施记录（2026-09-29）

本地 Git 已初始化，未配置远端。P0-01 的文件与版本证据见根目录 `global.json`、`Directory.*.props`、各项目及 lock files；直接依赖选择见 `README.md`。其余任务已编写主要实现，但完成条件含 Windows 交互桌面验证，故保留未勾选。具体状态：

| 任务 | 已落地的主要文件/证据 | 尚缺的完成条件 |
| --- | --- | --- |
| P0-02–03 | `src/GameSidebar.App/`、`.github/workflows/build.yml`、本机交叉发布 ZIP | Windows UI 真机启动、远端 CI 运行 |
| P1-01–02 | `src/GameSidebar.Core/`、`assets/profiles/`、Core tests | 更多配置故障回归 |
| P1-03–04 | `src/GameSidebar.Platform.Windows/` | Windows Geometry/DPI 真机核对 |
| P1-05–06 | `GameSessionManager.cs`、`WindowTracker.cs`、Application tests | Windows 长时及资源验收 |
| P1-07–09 | App ViewModel、Storage、Demo；Mac 进程启动 | Mac 界面逐项检查、Windows UI 流程、设置损坏测试 |
| P1-10–11 | `tools/GameSidebar.TestWindow/`、`docs/testing/phase1-acceptance.md` | Windows 受控窗口/无 SDK 解压/多 DPI/A20 |

本机 Release 构建 0 警告，Core 4 项与 Application 11 项通过，Windows 平台测试在 Mac 上明确 skipped。交叉发布文件位于被忽略的 `artifacts/`，需要交付时可复制；它们未在 Windows 运行。工程验收和真实游戏验收均尚未宣称完成。
