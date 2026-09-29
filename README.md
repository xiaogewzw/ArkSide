# GameSidebar · Phase 0–1 窗口绑定诊断

这是 Windows x64 窗口绑定与几何诊断程序。当前交付不包含侧边栏、截图、识别或输入自动化。`Ready` 只表示身份、Client 几何和 Profile 尺寸可用；目标游戏兼容性仍为 `Unknown`。

## 开发与运行

- SDK：.NET 10.0.401（`global.json` 固定）。
- Mac：`dotnet restore GameSidebar.sln --locked-mode && dotnet run --project src/GameSidebar.App`。界面常驻显示 `DEMO 模式`，候选窗口来自 Fake 服务。场景选择器可模拟无窗口、多候选、移动、尺寸变化、最小化、消失、重启及身份不足。
- Windows：运行 `GameSidebar.App.exe`，默认使用 Win32 实际窗口服务；`--demo` 可显式进入 Demo。也可用 SDK 执行 `dotnet run --project src/GameSidebar.App`。
- 受控窗口：单独运行 `GameSidebar.TestWindow.exe`；按 B 切换边框、S 切换 Client 1920×1080/1280×720、T 改标题、R 在同进程重建、Esc 关闭。重复启动可测试多候选。这个工具不在主程序 ZIP 内。

首次目标配置为空，自动搜索停用。可在界面刷新候选、选窗手动绑定；填写 `exe`（如 `GameSidebar.TestWindow.exe`）并保存后可自动搜索。当前游戏名称、`gameId`、真实 exe、标题和类名规则保持空值。

## 设置与 Profile

设置写入用户应用数据目录下的 `GameSidebar/settings.json`：Windows 为 `%LocalAppData%/GameSidebar`，Mac 为系统返回的 LocalApplicationData 目录。损坏 JSON 会备份成 `.corrupt-时间`，界面提示并使用默认值。仅持久化目标规则、Profile 偏好与诊断设置，不保存 PID、HWND 或会话快照。

目标规则字段：`schemaVersion=1`、`gameId`、`displayName`、`exe`、`titleRule`、`classRule`、`preferredProfileId`。`exe` 为文件名，大小写不敏感；标题与类名为可选正则，有 100ms 匹配超时。内置 `profiles/diagnostic-1920x1080.json` 只按 Client 物理像素尺寸诊断，`purpose=DiagnosticOnly`，不代表游戏布局验证。另见 [目标配置模板](docs/testing/target-settings-template.json)。

日志在应用数据目录的 `logs/game-sidebar.log`，最多约 3×2 MB。状态转换及错误关联 SessionId/BindingGeneration，普通轮询不写完整几何。诊断 JSON 可从界面复制，不用它恢复下次绑定。

## 构建与发布

直接依赖版本集中在 `Directory.Packages.props`；生产项目均为 `net10.0`。Core 无 Avalonia/Win32 依赖。默认 lock files 用于跨平台构建，`packages.win-x64.lock.json` 用于 RID 发布；工作流在发布前复制相应锁文件并使用 locked restore。

```sh
dotnet restore GameSidebar.sln --locked-mode
dotnet build GameSidebar.sln -c Release --no-restore
dotnet test GameSidebar.sln -c Release --no-build --no-restore
```

`.github/workflows/build.yml` 使用 `macos-15` 与 `windows-2025`，固定 SDK，上传测试结果和独立 ZIP。[GitHub Releases](https://github.com/xiaogewzw/ArkSide/releases) 提供可下载的 Windows 验收候选 ZIP；源码 ZIP 不能直接运行。本机 Mac 可交叉生成 self-contained win-x64 ZIP，但它不能替代 Windows 桌面启动与 DPI 验收。详见 [验收记录](docs/testing/phase1-acceptance.md)和 [Windows 逐项验收清单](docs/testing/windows-phase1-checklist.md)。

## 依赖基线（2026-09-29）

| 项目 | 固定版本 |
| --- | --- |
| .NET SDK | 10.0.401 |
| Avalonia / Desktop / Fluent | 12.1.3 |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Extensions.Hosting / Logging.Console | 10.0.12 |
| Microsoft.NET.Test.Sdk | 18.10.1 |
| xunit / runner.visualstudio | 2.9.3 / 4.0.0 |

此阶段不引入 MaaFramework 或 OpenCvSharp。

## 已知限制

Windows 原生窗口和多 DPI 行为仍需在交互桌面验收；Windows 10 22H2 单独保留兼容目标。Mac Demo 验证托管应用逻辑，不代表 Windows API 已通过。目标游戏信息未提供，真实游戏验收 A20 保持待验收。
