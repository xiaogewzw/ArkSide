# Phase 1 验收记录

日期：2026-09-29。源码位置：本地 `/Users/xiaoge/Developer/ArkSide`；GitHub 仓库：<https://github.com/xiaogewzw/ArkSide>。测试机：macOS 27.0 / arm64，.NET SDK 10.0.401。Windows 设备、OS build、缩放和显示器拓扑待记录。Windows 逐项操作见 [真机验收清单](windows-phase1-checklist.md)。

## 工程验证

| 项目 | 结果 | 证据/限制 |
| --- | --- | --- |
| Mac Release 构建 | 通过 | `dotnet build GameSidebar.sln -c Release -p:ContinuousIntegrationBuild=true --no-restore`，0 警告 |
| Mac Core / Application 测试 | 通过 | 4 + 20 项；含负坐标、Profile 歧义、迟到结果、解绑、非重入轮询、发现 0/1/2 候选、正则超时、尺寸/最小化/恢复和停止搜索语义 |
| Windows CI 平台测试 | 通过 | Windows runner 上 Core 4、Application 20、Windows 平台 1 项全部通过；交互桌面检查仍待真机 |
| Mac Demo 主窗口启动 | 通过（界面操作待核对） | 本机进程日志出现 `Main window opened in Demo mode` 与 `Starting -> NotConfigured`；图形自动化入口超时，未完成界面逐项操作 |
| 本机交叉发布 | 通过 | self-contained win-x64 主程序（SHA256 `f3f0904d3d1846e7cb1eb8a032030164a851a3bb7634b1c571eb9277a1d59190`）和 TestWindow（`8fa6c718c6fe0cde8d4620a55b04b9cc64e4cf33183de8f89888d3b56eb8cf6c`）分包，核对 exe 与内置 Profile；这些是本机产物，Release 以其自身 `SHA256SUMS.txt` 为准 |
| GitHub Actions | 通过 | [运行 36579227426](https://github.com/xiaogewzw/ArkSide/actions/runs/36579227426)：Mac/Windows 构建和测试、Windows self-contained 发布及 ZIP 内容检查全绿；不包含交互桌面验收 |
| Windows 无 SDK 解压启动 | 待执行 | 需 Windows 干净环境 |
| Windows 10 22H2 | 待执行 | 兼容目标保留 |
| 真实目标游戏 | 待执行 | gameId/exe/规则未提供 |

## A01–A24

`自动通过` 仅指托管逻辑测试；`待 Windows` 不能视为工程验收通过。

| 编号 | 当前状态 | 下一步/证据 |
| --- | --- | --- |
| A01 | 部分通过 | 空配置单元测试和 Mac 启动日志；Windows UI 待验 |
| A02 | 部分通过 | 自动发现 0/1/2 候选测试；Windows 多实例待验 |
| A03 | 待 Windows | 列表选中后关闭 TestWindow |
| A04 | 待 Windows | 普通边框 1920×1080 Client |
| A05 | 部分通过 | 托管测试覆盖绑定不变时尺寸不支持→恢复和 GeometryVersion；Windows UI 待验 |
| A06 | 待 Windows | 连续移动/resize 20 次，记录 500ms 延迟分布 |
| A07 | 待 Windows | 100%、125%、150% DPI |
| A08 | 待 Windows | 混合 DPI 与负坐标显示器 |
| A09 | 部分通过 | Fake 测试覆盖最小化旧几何 Stale 和恢复；Windows 真机待验 |
| A10 | 待 Windows | 前台标记变化 |
| A11 | 待 Windows | 自动重绑与手动不重绑；分进程重启/同进程重建 |
| A12 | 自动通过 | `Late_A_result_cannot_replace_B` |
| A13 | 自动通过（模拟） | `Reused_window_identity_invalidates_the_old_session`；真机复用未声称复现 |
| A14 | 自动通过 | `Manual_unbind_disables_automatic_rebind_and_stops_tracking` |
| A15 | 部分通过 | Fake 发现错误与候选权限失败被区分；Windows 权限/原生查询场景待验 |
| A16 | 自动通过 | 设置损坏备份、无效 Profile/规则、无效正则与正则超时测试 |
| A17 | 待 Windows | 50 次绑定/解绑及资源趋势 |
| A18 | 待 Windows | 连续 30 分钟及日志上限 |
| A19 | 待 Windows | 无 SDK 解压启动，读 Profile 并绑定 TestWindow |
| A20 | 待目标信息 | 真正游戏验证独立记录 |
| A21 | 自动通过 | `Ambiguous_profile_selection_keeps_binding_and_can_recover` |
| A22 | 部分通过 | Fake 测试覆盖停止搜索保留绑定、目标丢失后不再搜索；Windows 验证待做 |
| A23 | 自动通过（Fake） | `Identity_information_can_recover_on_the_next_sample` |
| A24 | 待 Windows | DWM 可见外框失败回退记录 |

## Windows 手工记录模板

- 设备与 OS edition/build：
- CPU 架构、DPI（100/125/150%）、显示器拓扑与负坐标：
- App/TestWindow ZIP SHA256：
- Client 物理尺寸、WindowBoundsPx、ClientOriginScreenPx、MonitorBoundsPx：
- DWM 来源、目标/调用方 awareness：
- A06 20 次更新延迟、超 500ms 原因：
- A17 50 次绑定后句柄/内存/进程趋势：
- A18 30 分钟日志大小与资源趋势：
- 失败用例、Native 错误码、诊断 JSON：

## 当前交接结论

本机完成工程构建、托管逻辑回归、Demo 启动和 win-x64 交叉打包；GitHub Windows CI 也通过编译、自动测试与 ZIP 打包。Windows 受控窗口绑定、多 DPI、无 SDK 启动和长时运行未验证，Phase 1 工程验收尚未完成；真实游戏 A20 另列待验收。
