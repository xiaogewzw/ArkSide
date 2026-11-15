# ArkSide

ArkSide 是明日方舟的桌面侧栏助手：连接游戏窗口，在盟约确认页面截图并离线识别禁用盟约，结合干员数据展示推荐，支持标签、副盟约和 Rank 筛选。

识别在本机完成，发布包自带 .NET、Python 运行时和 OCR 模型，无需额外安装这些依赖。游戏需要自行启动。

## 下载

到 [GitHub Releases](https://github.com/xiaogewzw/ArkSide/releases) 下载对应平台的应用 ZIP。`v1.0.0` 为本次准备发布的首个正式版本；发布前，Releases 中的 B2 / Phase 1 是旧测试版。

| 平台 | v1.0.0 应用附件 |
| --- | --- |
| Windows x64 | `ArkSide-v1.0.0-Windows-x64.zip` |
| macOS Apple Silicon | `ArkSide-v1.0.0-macOS-arm64.zip` |

下载应用 ZIP 和 `SHA256SUMS.txt`，核对哈希后完整解压。GitHub 自动提供的 **Source code** 是源码，不是可直接运行的应用。本次不提供 Intel Mac、Windows ARM 或 Linux 包。

在 Windows PowerShell 中校验：

```powershell
Get-FileHash .\ArkSide-v1.0.0-Windows-x64.zip -Algorithm SHA256
```

在 macOS 中校验：

```sh
shasum -a 256 ArkSide-v1.0.0-macOS-arm64.zip
```

## 启动和识别

1. Windows：完整解压，进入 `ArkSide` 文件夹，双击 `ArkSide.exe`。保留同目录下的所有文件。
2. macOS：解压后将 `ArkSide.app` 移到“应用程序”，双击启动。首次截图时，在“系统设置 → 隐私与安全性 → 录屏与系统录音”中允许 ArkSide，然后重启助手。
3. 启动明日方舟。助手默认按 Windows 的 `Arknights.exe` 或 macOS 的 `com.hypergryph.arknights` 查找游戏，找到后吸附到游戏窗口右侧。需要时点击“重新连接”。
4. 游戏进入所有盟约都显示完整的页面，点击“开始识别”。完成后按标签、副盟约、Rank 和显示排数查看推荐。
5. 游戏内更换盟约后，再次点击“开始识别”。识别期间和识别失败后会保留上次成功结果；只有识别成功才整体替换，点击“清空数据”可主动清除。

侧栏宽度固定，可以拖动底部调整高度，也可以收起和展开。退出或重新打开游戏后可重新连接，已有结果在本次助手会话内保留。

当前 macOS 包使用本地 ad-hoc 签名，未做 Developer ID 签名和 Apple 公证。网络下载后的首次打开可能被系统拦截；确认下载来源后，可按系统提示在“隐私与安全性”中允许打开。应用包声明最低 macOS 14.0，旧系统和不同硬件的兼容性尚未做完整覆盖。

## 模拟器或自定义目标

正式侧栏没有窗口设置入口。Windows 使用模拟器或其他进程名时，在 `%LocalAppData%\ArkSide\target-game.json` 写入实际承载游戏窗口的 exe 完整路径，然后重启助手：

```json
{
  "schemaVersion": 1,
  "windows": {
    "executablePath": "D:\\Emulator\\GameWindow.exe"
  }
}
```

替换为本机真实路径；多个匹配窗口时，可增加 `titleRule` 或 `classRule` 缩小范围。配置格式见 [目标配置示例](https://github.com/xiaogewzw/ArkSide/blob/main/assets/target-game.example.json)。

设置、目标配置和日志保存在当前用户本地应用数据目录中的 `ArkSide` 文件夹，日志在其 `logs` 子目录。Windows 为 `%LocalAppData%\ArkSide`，macOS 为 `~/Library/Application Support/ArkSide`。更新应用时保留此目录，应用安装目录中的文件需整套更新。

## 常见问题

- **没有找到游戏**：先启动游戏，再点击“重新连接”；模拟器场景检查上面的目标配置。
- **没有截图权限**：在 macOS 系统设置中授权录屏，之后重启助手。
- **截图异常**：将游戏恢复为可见窗口，确认目标进程正确后重试。
- **请将游戏画面调整到所有盟约都显示出来**：当前画面不完整或无法可靠识别，返回完整盟约页面后重试。
- **干员详细资料异常**：重新完整解压应用，确认 `recognition/data` 中的 JSON 和头像目录存在。

反馈问题请到 [Issues](https://github.com/xiaogewzw/ArkSide/issues)，提供应用版本、系统版本、游戏运行方式、复现步骤，以及对应时间的日志片段。包内 `release.json` 记录版本和源码提交，Mac 位于 `ArkSide.app/Contents/Resources/release.json`，Windows 位于 `ArkSide/release.json`。分享截图和日志前，请去除账号及个人路径信息。

## 文档和资源来源

- [更新记录](https://github.com/xiaogewzw/ArkSide/blob/main/CHANGELOG.md)
- [构建、验收与发布指南](https://github.com/xiaogewzw/ArkSide/blob/main/RELEASING.md)
- [Windows 正式版测试清单](https://github.com/xiaogewzw/ArkSide/blob/main/Windows-%E6%AD%A3%E5%BC%8F%E7%89%88%E6%B5%8B%E8%AF%95%E6%B8%85%E5%8D%95.md)
- [离线识别说明和回归记录](https://github.com/xiaogewzw/ArkSide/blob/main/tools/offline_recognition/README.md)
- [开发与历史验收记录](https://github.com/xiaogewzw/ArkSide/blob/main/DEVELOPMENT.md)
- [第三方依赖与资源来源](https://github.com/xiaogewzw/ArkSide/blob/main/THIRD_PARTY_NOTICES.md)

ArkSide 图标和干员 JSON 由项目作者制作，干员头像取自 [PRTS Wiki](https://prts.wiki/)。相关游戏素材的权利归各自权利人。项目目前未添加项目级开源许可证，第三方组件仍遵循各自许可证。
