# Phase 1 Windows 真机验收清单

此清单用于 Windows x64 交互桌面。逐项填写“通过 / 失败 / 未执行”，附诊断 JSON、截图或日志；未执行不能记为通过。A20 真实游戏验收独立于受控测试窗口。

## 1. 环境与产物

- 系统：记录 Windows 版本、Edition、OS build、x64 CPU；至少验证一台 Windows 11，另在 Windows 10 22H2 上做兼容复测。没有 Windows 10 设备时保持“未执行”。
- 权限：普通用户交互桌面即可；不要以管理员身份运行一个程序、普通身份运行另一个程序来替代标准路径。DPI 用例需要可调缩放；A08 需要两台不同缩放的显示器，副屏能放在主屏左侧。没有对应设备时标“未执行”。
- SDK：主路径使用 self-contained ZIP，无需安装 .NET SDK 或 Runtime。A19 必须在**未安装 .NET SDK/Runtime** 的 Windows 环境验证；其他用例可在同一环境完成。
- 从 [GitHub Releases](https://github.com/xiaogewzw/ArkSide/releases) 下载同一版本的 `GameSidebar-win-x64.zip`、`TestWindow-win-x64.zip` 和发布页上的 SHA256 值。分别解压到两个独立空目录，不要只下载 GitHub 的“Source code” ZIP；源码 ZIP 不含可运行程序。

PowerShell 示例（在两个 ZIP 所在目录执行，填入发布页的实际 SHA256）：

```powershell
Get-FileHash .\GameSidebar-win-x64.zip -Algorithm SHA256
Get-FileHash .\TestWindow-win-x64.zip -Algorithm SHA256
Expand-Archive .\GameSidebar-win-x64.zip .\GameSidebar -Force
Expand-Archive .\TestWindow-win-x64.zip .\TestWindow -Force
Test-Path .\GameSidebar\GameSidebar.App.exe
Test-Path .\GameSidebar\profiles\diagnostic-1920x1080.json
Test-Path .\TestWindow\GameSidebar.TestWindow.exe
Start-Process .\TestWindow\GameSidebar.TestWindow.exe
Start-Process .\GameSidebar\GameSidebar.App.exe
```

预期三个 `Test-Path` 均为 `True`，两个进程能启动。Windows Defender/SmartScreen 如提示未知发布者，记录提示与处理结果；当前 ZIP 未承诺代码签名。主程序顶部应显示真实 Windows 模式，而不是 `DEMO 模式`。日志在 `%LocalAppData%\GameSidebar\logs\game-sidebar.log`，设置在 `%LocalAppData%\GameSidebar\settings.json`。复制诊断 JSON 前可核对是否含本机敏感路径或窗口标题。

## 2. 基础操作

TestWindow 的快捷键需在**该窗口获得焦点**时按：`B` 切换有边框/无边框，`S` 切换 Client 1920×1080/1280×720，`T` 更改标题，`R` 在同一进程重建窗口，`Esc` 关闭。启动第二个 `GameSidebar.TestWindow.exe` 可制造两个候选。移动/调整窗口直接用鼠标。测试窗口标题带有 Client 物理尺寸、坐标和 PID，供与主程序诊断对照。

首次打开主程序时，目标 exe 为空。点击“刷新窗口”→选择 TestWindow→“绑定所选”；绑定后读取状态、SessionId、BindingGeneration、GeometryVersion、ClientSizePx、ClientOriginScreenPx、VisibleFrameSource、DPI 和 Profile。测试自动搜索时在 exe 输入框填 `GameSidebar.TestWindow.exe`、点击“保存目标设置”→“开始自动搜索”。只填 exe 即可；标题/类名正则保持空白。重置空配置时清空 exe 并保存。

## 3. 逐项验收标准

| 编号 | 操作与通过标准 | 记录 |
| --- | --- | --- |
| A01 | 清空 exe 并重启：显示 `NotConfigured`，不自动全匹配；仍可刷新、手动选窗绑定。 | |
| A02 | 配置 TestWindow exe，分别保持 0/1/2 个实例：自动搜索依次等待、绑定唯一候选、要求选窗；候选枚举顺序变化不应改变决定。 | |
| A03 | 刷新列表并选中目标，按 Esc 关闭后再点“绑定所选”：显示明确失败原因，可刷新重选，主程序不崩溃。 | |
| A04 | TestWindow 为有边框 1920×1080：`ClientSizePx=1920×1080`，Profile 匹配；`WindowBoundsPx` 可更大，不参与尺寸匹配。 | |
| A05 | 已绑定时按 S 切到 1280×720，再切回：同一 SessionId 保留；状态从 `UnsupportedResolution` 恢复 `Ready`，GeometryVersion 随实际几何改变。 | |
| A06 | 同屏移动/resize 20 次，每次记操作时刻与诊断更新时刻：正常负载下至少 19 次在 500ms 内更新；超时记录操作、耗时、错误原因。有效映射变化时 GeometryVersion 增，只有轮询时间变化时不增。 | |
| A07 | 分别设 100%、125%、150% 缩放并重启测试窗口：诊断 Client 物理尺寸与窗口标题一致，无重复缩放；记录 TargetWindowDpi、TargetAwareness、CallerAwareness。 | |
| A08 | 两屏设不同缩放，将副屏放主屏左侧并移入目标：ClientOriginScreenPx 可为负；位置、MonitorBoundsPx/WorkAreaPx 与系统布局一致；ClientSizePx 仍为物理像素。无设备则未执行。 | |
| A09 | 最小化目标再恢复：身份/SessionId 保留，最小化时不把旧几何当作有效数据（应为 Stale/不可用及明确状态）；恢复后重新验证并回到 Ready。 | |
| A10 | 切换其他应用为前台再切回：IsForeground 标记相应变化；主程序不主动激活 TestWindow。 | |
| A11 | 自动模式关闭目标并重新启动：旧会话失效，新窗口按规则重绑；手动模式同样操作后应等待手动绑定。另按 R 做同进程重建，分别记录旧/新 PID、HWND、SessionId。 | |
| A12 | 自动测试已覆盖慢 A/切 B；真机可观察切换后旧 A 的结果不覆盖 B，记录诊断。 | |
| A13 | PID/HWND 复用由 Fake 自动测试覆盖；真机无需强制造出系统复用。若自然发生，身份变化必须使旧会话失效。 | |
| A14 | 手动点击“解绑”：跟踪停止，不会自行重绑；重新手动绑定或显式开始搜索才恢复。 | |
| A15 | 对权限受限窗口或可复现原生查询失败场景刷新/绑定：错误含可理解原因及可用时的 Native 错误码，其他候选仍可操作，旧几何标为无效。无法稳定制造权限场景则保留 Windows 子项未执行。 | |
| A16 | 自动测试覆盖设置损坏、无效 Profile、无效正则和正则超时；Windows 上可额外输入 `[` 作为标题正则，错误应可见，改回空值后恢复，不持续卡住 UI。 | |
| A17 | 手动绑定/解绑 50 次并退出：无重复跟踪循环、残留主程序进程；每 10 次记一次任务管理器的句柄数和内存，不能出现随次数持续无界增长趋势。 | |
| A18 | 保持绑定运行 30 分钟，每 5 分钟记响应、内存、句柄与日志大小：程序仍响应、诊断继续更新；日志总量不超过约 6 MiB（3 个约 2 MiB 文件），资源无持续增长趋势。 | |
| A19 | 在无 SDK/Runtime 的 Windows 环境用两个 ZIP 解压启动：能读取内置 Profile 并手动绑定 TestWindow 达到 Ready。记录 `dotnet --info` 的结果或系统已安装程序清单。 | |
| A20 | 提供目标游戏的 exe、窗口规则和测试设备后另测自动/手动绑定、几何、尺寸与重启；当前不计受控窗口通过。 | |
| A21 | 自动测试已覆盖同尺寸多 Profile 歧义；若 Windows 手动增设第二个同尺寸 Profile，应出现 Profile `NeedsSelection` 且保留绑定，选偏好后恢复。 | |
| A22 | 自动绑定后点击“停止搜索”：当前目标继续更新；随后关闭目标，不自动绑定新实例。 | |
| A23 | 进程创建信息暂不可读由 Fake 自动测试覆盖；Windows 若自然遇到，显示 BoundUnavailable，恢复后重新验证，不沿用缓存身份。 | |
| A24 | DWM 可见外框读取失败时诊断 `VisibleFrameSource=WindowBoundsFallback`，`VisibleFrameBoundsPx=WindowBoundsPx`，Client 尺寸判断继续有效。若真机无法稳定触发，保留 Windows 子项未执行并附实际 DWM 来源。 | |

## 4. 结果记录与工程门槛

记录：设备/OS build/CPU、ZIP 文件名与 SHA256、屏幕分辨率/缩放/拓扑、每项状态、诊断 JSON、错误码、日志路径与截图。A06 附 20 次耗时原始表；A17 附第 0/10/20/30/40/50 次资源值；A18 附 0/5/10/15/20/25/30 分钟资源与日志值。失败时记录复现步骤及对应时间，便于关联日志。

工程门槛：GitHub Actions 的 Mac/Windows 构建与自动测试通过；A01–A11、A14、A17–A19、A22 的 Windows 受控窗口项目通过；A15、A24 的真机异常路径若无法复现，明确列未执行并保留 Fake/代码证据；A07/A08 缺显示设备时保持未执行。任何必需项失败或未执行时，不宣称 Phase 1 工程验收完成。A20 真实游戏须另行验收，不能用 TestWindow 代替。
