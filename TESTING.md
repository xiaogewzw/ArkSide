# v1.0.0 发布准备验证

记录日期：2026-11-15。用户反馈应用在 macOS 和 Windows 环境的测试已经基本通过；未提供完整的系统版本和逐项 PASS / FAIL 记录，因此本文不将未汇总的项目声明为全部验收通过。

## 已有基线

源码基线：`aca2f931c46e445601c20dadde2620ac8fdeb947`。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| GitHub macOS / Windows 构建与测试 | 成功 | [Build and test 43611524089](https://github.com/xiaogewzw/ArkSide/actions/runs/43611524089) |
| GitHub Windows 正式包、识别回放和启动检查 | 成功 | [Package ArkSide 43611524086](https://github.com/xiaogewzw/ArkSide/actions/runs/43611524086) |
| 本机 .NET Release 测试 | 82 通过、0 失败、1 项 Windows 原生 API 测试在 Mac 上跳过 | 2026-11-15 本机复核 |
| 本机离线识别回归 | 33 通过、0 失败、0 错误 | 2026-11-15 本机复核；输出在 `artifacts/release-checks/pre-release-review-20261115/` |
| 历史 Mac 发布包 worker 回放 | 36 项通过 | [v4 发布包报告](tools/offline_recognition/verification/v4/packaged-verification.json) |
| 历史 Mac 发布包 C# 服务调用 | 6 项通过 | [v4 服务报告](tools/offline_recognition/verification/v4/dotnet-service.json) |

Windows 游戏截图在 Mac 上回放只验证识别算法；其结果不等于 Windows 原生窗口采集验证。历史报告的执行时间及 worker 哈希仅对应当时构建。

## 本次发布配置改动

本次调整版本、文档和构建发布流程，不更改识别算法或侧栏交互。提交后的自动验证以该提交的 [Actions](https://github.com/xiaogewzw/ArkSide/actions) 结果为准，正式发布前仍需从草稿下载最终附件验证。

2026-11-15 对发布准备改动执行的本机检查：

| 检查 | 结果 |
| --- | --- |
| NuGet 锁定还原、CI 严格模式 .NET Release 测试 | 82 通过、0 失败、1 项 Windows 原生测试在 Mac 上跳过 |
| 正式界面编译（`ArkSideProduction=true`） | 0 警告、0 错误 |
| 三份 GitHub 工作流的 actionlint 检查 | 通过 |
| 版本与产物来源检查 | 一致来源可生成校验和；错误标签、版本、未提交状态、不同提交和包内来源不一致均被拒绝 |
| 新 Mac 包的实际 worker 回放 | 36 项通过 |
| 新 Mac 包的 C# 识别服务调用 | 3 项通过 |
| Mac 签名、Bundle 版本和附带说明 | 签名验证通过；版本 1.0.0；用户说明、第三方声明与来源记录均已包含 |

上述本机 Mac 包在提交前生成，`release.json` 如实标记 `source_dirty: true`，仅用于本次构建验证。正式发布工作流会在干净的最终提交上重新构建，并拒绝未提交状态的产物。构建日志与报告保存于本机 `artifacts/release-checks/`，不提交生成的应用或 ZIP。

## 真机与最终下载包记录

[Windows 正式版测试清单](Windows-正式版测试清单.md) 是可重复使用的验收模板，保留“未执行”占位，由测试者按本次实际操作填写。

| 项目 | macOS | Windows |
| --- | --- | --- |
| 已有应用整体测试反馈 | 用户反馈基本通过 | 用户反馈基本通过 |
| 操作系统版本、硬件、显示缩放 | 待测试者补充 | 待测试者补充 |
| 本次最终版本标签 / 提交 SHA | 发布前补充 | 发布前补充 |
| 浏览器下载、校验和首次启动 | 最终附件生成后确认 | 最终附件生成后确认 |
| 游戏绑定、实时识别和筛选 | 最终附件生成后确认 | 最终附件生成后确认 |
| 错误后恢复、退出与重启 | 最终附件生成后确认 | 最终附件生成后确认 |

发布时保留真实未执行项目及已知问题，勿根据 CI 成功代填真机项目。
