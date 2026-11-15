# 构建、验收与发布

## 当前版本与构建来源

`Version.props` 是版本号的唯一配置来源，当前为 `1.0.0`。.NET 程序集、macOS Bundle 版本和 ZIP 文件名从此配置生成。每个应用包附 `release.json`，记录版本、源码提交、平台和构建时是否存在已跟踪文件的未提交改动。

macOS 的 `CFBundleShortVersionString` 与 `CFBundleVersion` 使用版本号的数字部分；若将来采用 `1.0.1-rc.1`，完整候选版本仍记录在 .NET 信息版本、ZIP 名和 `release.json` 中。

构建工具为 `global.json` 固定的 .NET SDK 10.0.401、Python 3.12，以及各平台的原生构建工具。NuGet 使用 `packages*.lock.json` 和 `--locked-mode`；Python 依赖版本见 `tools/offline_recognition/requirements-build.txt`。

## 本地构建

从仓库根目录执行：

```sh
dotnet restore GameSidebar.sln --locked-mode
dotnet build GameSidebar.sln -c Release --no-restore
dotnet test GameSidebar.sln -c Release --no-build --no-restore
```

macOS Apple Silicon 需要 Xcode 命令行工具和 Python 3.12：

```sh
python3.12 -m venv .venv-recognition
.venv-recognition/bin/python -m pip install -r tools/offline_recognition/requirements-build.txt
.venv-recognition/bin/python -m tools.offline_recognition.verify --output-dir artifacts/release-checks/macos-regressions
tools/package-macos.sh
```

输出为 `artifacts/release/ArkSide.app`、`ArkSide-v1.0.0-macOS-arm64.zip`、同名 `.sha256` 和 `macOS-arm64-release.json`。包采用 ad-hoc 签名；脚本验证签名、实际运行包内识别组件回放截图，并通过发布包的 C# 服务检查调用链，全部成功后才替换旧应用。

Windows x64 在 Windows PowerShell 中执行：

```powershell
python -m pip install -r tools/offline_recognition/requirements-build.txt
python -m tools.offline_recognition.verify --output-dir artifacts/release-checks/windows-regressions
./tools/package-windows.ps1
```

输出位于 `artifacts/release/windows/`：`ArkSide` 文件夹、`ArkSide-v1.0.0-Windows-x64.zip`、同名 `.sha256` 和 `Windows-x64-release.json`。脚本要求输出目录不存在；再次打包时指定新的空目录，例如 `./tools/package-windows.ps1 -OutputDirectory artifacts/release/windows-next/ArkSide`，保留旧包用于对照。脚本会回放识别用例、检查发布包 C# 服务，并从临时工作目录启动自包含程序验证存活。

两个脚本都会附带用户说明和第三方来源说明。离线 worker 必须在目标平台构建，不能用 macOS 交叉生成 Windows 的 Python 原生组件。

## GitHub 工作流

| 工作流 | 触发条件 | 结果 |
| --- | --- | --- |
| `Build and test` | 分支推送、PR、手动运行 | macOS / Windows 自动测试与正式界面编译检查 |
| `Package ArkSide` | `main` / `codex/package-arkside` 推送、手动运行或发布流程调用 | 两平台正式包、校验文件、来源记录和验证报告 |
| `Draft ArkSide release` | `v*` 标签推送，或指定已有标签手动运行 | 检查标签后构建两平台，创建草稿 Release，等待维护者公开 |

`Package ArkSide` 运行页的应用附件为 `ArkSide-macOS-arm64` 和 `ArkSide-Windows-x64`，解压 Actions 附件后才能取得应用 ZIP。`ArkSide-*-verification` 只包含报告，不是应用。报告中的自动测试、截图回放和启动存活检查不能代替真实游戏验收。

旧的固定 B2 发布工作流已移除；旧 Releases 及其附件保留，历史实现可在 Git 历史中查阅。

## 维护者发布步骤

本次准备工作止于当前分支提交并推送；以下步骤由维护者执行。

1. 创建 `codex/package-arkside` 到 `main` 的 PR，检查差异，并确认双平台 `Build and test` 和 `Package ArkSide` 成功。
2. 合并 PR，同步本地 `main`。本地 `main` 若与远端分叉，应先检查历史并妥善同步，不使用强制推送覆盖远端。
3. 更新本版本更新记录、发布说明和真实游戏测试记录，确保它们与最终待发提交一致；确认工作区干净。
4. 在最终发布提交上创建标签并推送。对于当前版本：

   ```sh
   git tag -a v1.0.0 -m 'ArkSide v1.0.0' <最终发布提交SHA>
   git push origin v1.0.0
   ```

5. `Draft ArkSide release` 要求标签与 `Version.props` 一致，且提交已在远端 `main` 的历史内。工作流对两个包的版本、源码提交和未提交状态进行检查，生成统一 `SHA256SUMS.txt`，只创建草稿。
6. 在草稿中下载两个最终 ZIP，分别确认校验、首次启动、游戏绑定、识别和筛选、失败后恢复及退出。macOS 要实际经过浏览器下载，确认首次打开与录屏授权流程。Windows 按 [正式版测试清单](Windows-正式版测试清单.md) 留记录。
7. 编辑草稿说明，补上实际测试的系统版本和已知问题，去掉维护者待办句；附件确认齐全后，手动点击 Publish release，并将正式版设为 Latest。

如果草稿生成失败，先查失败步骤；相同标签已有草稿时，创建步骤会拒绝重复创建。不要移动已对外发布的版本标签；修复后增加补丁版本，例如 `1.0.1`。工作流支持手动对已有标签重跑，但不会自动覆盖已有 Release 附件。

## 验证记录

发布准备验证与真机反馈见 [TESTING.md](TESTING.md)。离线回归历史保存在 `tools/offline_recognition/verification/v2`、`v3`、`v4`；每次重新执行产生的大量截图和日志写入被 Git 忽略的 `artifacts/`。
