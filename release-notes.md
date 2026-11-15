明日方舟桌面侧栏助手的首个正式版本，提供本地盟约识别、干员推荐以及标签、副盟约和 Rank 筛选。再次识别期间及失败后保留上次成功结果，成功后替换，也可手动清空。

## 下载与运行

- Windows x64：下载 `ArkSide-v1.0.0-Windows-x64.zip`，完整解压后进入 `ArkSide` 文件夹，双击 `ArkSide.exe`。
- macOS Apple Silicon：下载 `ArkSide-v1.0.0-macOS-arm64.zip`，解压后将 `ArkSide.app` 移入“应用程序”。首次截图需要授予录屏权限并重启助手。
- 使用 `SHA256SUMS.txt` 核对下载文件。无需额外安装 .NET 或 Python。GitHub 的 Source code ZIP 是源码，不是运行包。

## 使用与限制

先启动游戏，助手连接后进入所有盟约可见的页面，点击“开始识别”。游戏内更换盟约后需再次识别。Windows 模拟器或其他进程名的配置方法、问题反馈和日志位置见 [README](https://github.com/xiaogewzw/ArkSide#readme)。

macOS 包使用 ad-hoc 签名，未做 Developer ID 签名与 Apple 公证；从网络下载后的首次启动可能需要在系统设置中允许打开。本次仅提供 Windows x64 和 macOS arm64 包，完整系统兼容矩阵尚未覆盖。

图标和干员 JSON 由项目作者制作，头像来自 [PRTS Wiki](https://prts.wiki/)。依赖及素材来源见包内 `THIRD_PARTY_NOTICES.md`。包内 `release.json` 记录版本、源码提交和平台。

维护者公开此草稿前，请检查双平台最终下载包，并补充实际测试的系统版本及已知问题。
