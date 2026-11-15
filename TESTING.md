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

## 外置干员 JSON 支持（2026-11-15）

正式应用改为从用户本地应用数据目录 `ArkSide/recognition/data` 读取两份 JSON。包内数据保留为首次运行的默认模板，头像和模型继续由应用包提供。新增验证：

| 检查 | 本机 macOS 结果 |
| --- | --- |
| .NET Release 测试及正式界面编译 | 88 项通过、0 失败、1 项 Windows 专用测试跳过；正式构建 0 警告、0 错误 |
| 外部存储回归 | 首次初始化、升级保留修改、单文件恢复、无效 JSON 保留、外部规则与名单一致、多实例并发初始化，共 6 项通过 |
| 新 Mac 包的基线识别回放与 C# 服务调用 | 36 项回放、3 项服务调用通过 |
| 实际外部 JSON 调用链 | 在含中文和空格的临时目录中修改标签，并将干员数量从 141 改为 140；C# 推荐数据和冻结 OCR worker 均读取到外部的 140 名干员 |
| 数据与资源分离 | 自定义标签已加载、用户文件保持原样、缺失规则自动补齐、头像仍来自包内、两份包内 JSON 哈希均未变化 |
| 外部修改后的 Mac 签名 | 打包脚本在外部配置验证后再次执行 `codesign --verify --deep --strict`，通过 |

本机日志位于 `artifacts/release-checks/external-data/`，外部数据检查报告为 `artifacts/release-checks/macos-external-data/verification.json`。相同外部数据检查已加入 Windows 和 macOS 打包脚本，Windows 原生执行结果以本次提交后的 CI 为准。本轮使用保存截图回放，没有操作实时游戏窗口或修改真实用户配置目录。

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
