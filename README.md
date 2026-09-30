# GameSidebar · B2 双平台侧边栏与截图

GameSidebar 是面向 Windows x64 和 Apple Silicon Mac 的窗口绑定与截图诊断程序。本轮实现了外部目标配置、真实窗口绑定、右侧侧边栏、单帧 PNG 和 1–5 FPS 预览。真实鼠标/键盘操作、OCR、长图和游戏业务识别属于后续批次。

## 运行

Windows 测试者可从 [B2 测试版发布页](https://github.com/xiaogewzw/ArkSide/releases/tag/b2-windows-acceptance-2026-09-30) 下载主程序和 TestWindow ZIP，按 [Windows B2 轻量测试清单](Windows-B2-测试清单.md) 验收。测试清单也附在主程序 ZIP 内；无需安装 SDK。Windows 包由 GitHub Windows runner 编译，截图与预览需真机另行确认。

使用 .NET SDK 10.0.401：

```sh
dotnet restore GameSidebar.sln --locked-mode
dotnet build GameSidebar.sln -c Release --no-restore
dotnet test GameSidebar.sln -c Release --no-build --no-restore
```

- Windows：运行发布 ZIP 内的 `GameSidebar.App.exe`。Maa Win32 Controller 使用 FramePool / PrintWindow 候选截取目标窗口；`--demo` 显式运行模拟窗口与模拟帧。
- macOS：运行 `tools/package-macos.sh` 生成 `artifacts/GameSidebar-Dev.app`，再用 `ditto artifacts/GameSidebar-Dev.app ~/Applications/GameSidebar-Dev.app` 安装到固定开发位置并从 Finder 启动。开发 Bundle ID 固定为 `dev.arkside.GameSidebar`。首次截图请在系统设置的“隐私与安全性 → 录屏与系统录音”允许此 App，必要时重新启动。应用选择、窗口枚举、位置由原生桥接提供；截图先用 Maa ScreenCaptureKit Controller，失败时尝试 ScreenCaptureKit 桥接，再使用系统窗口截图工具。侧边栏状态显示实际方法。
- Demo：`dotnet run --project src/GameSidebar.App -- --demo`，只用于 UI 和会话逻辑；不会自动代替真实窗口模式。

Windows 包与 Mac `.app` 都附 `target-game.example.json`，没有默认的活动 `target-game.json`。两平台游戏先由用户正常启动；可直接在候选窗口列表手动绑定。

## 目标配置

活动文件默认与 Windows exe 或 Mac `.app` 同级。开发目录或只读目录可用 `--target-config /绝对路径/target-game.json` 指定。UI 显示实际来源，可“重新加载”；编辑后“保存目标”写回当前外部文件，失败时可“另存目标配置”。另存到非默认位置后，下次启动需显式传 `--target-config`。

```json
{
  "schemaVersion": 1,
  "gameId": null,
  "displayName": null,
  "windows": {
    "executablePath": "D:\\Games\\ExampleGame\\Game.exe",
    "titleRule": null,
    "classRule": null,
    "preferredProfileId": "diagnostic-1920x1080"
  },
  "macOS": {
    "appBundlePath": null,
    "bundleId": null,
    "titleRule": null,
    "preferredProfileId": null
  }
}
```

Windows 要填写拥有游戏窗口的 exe **完整路径**；从运行窗口回填可避免误填启动器。明日方舟运行在模拟器中时，应选择实际承载游戏画面的模拟器窗口，从所选窗口回填路径后保存，再开始自动搜索。多个匹配窗口可手动选择，必要时增加标题规则。完整路径不可读时不按同名文件回退。macOS 可选择 `.app` 或从运行窗口回填 Bundle ID 和路径；应用移动后仍可按 Bundle ID 搜索运行中的窗口。

加载优先级是显式路径、同级 `target-game.json`、旧应用数据目录内 `settings.json` 的 target。前两项存在但损坏时会报错，不静默使用旧目标。外部文件当前平台字段为空时显示未配置。`settings.json` 仍保存旧目标兼容信息和其他设置；日志位置不变。个人活动配置已加入 `.gitignore`。

## 侧边栏与诊断

绑定真实窗口后，侧边栏在目标右侧出现。可收起至 48 DIP、展开至 360 DIP、截图、选择路径保存 PNG、开始或停止低帧率预览。最小化、解绑或目标丢失时隐藏/暂停。截图结果带会话代次与几何版本，换绑后的旧帧被丢弃。Profile 不匹配仍可预览窗口，布局分析和输入尚未开放。

macOS 窗口位置以 desktop point 记录，Retina 截图以 PNG 像素报告；Client 映射未知时界面明确标记未知。Windows 保留原有 Client 物理像素与 DPI 诊断。

## 当前验证状态（2026-09-30）

- Mac 本机构建、固定目录安装和 Finder 启动：已验证，构建/安装二进制哈希一致。
- 授权后的 Maa ScreenCaptureKit 已实际截取 Finder（1651×720）和明日方舟（1097×720），从应用保存 PNG 并检查完整画面。Finder 预览约 4 分钟，游戏预览约 3 分钟；停止、目标最小化/恢复、关闭/重新绑定、游戏退出和预览运行时程序退出均已实测。最终应用选择器修复版已再次授权，实际取得 Finder/游戏 PNG，Finder 和游戏预览复验均超过两分钟，停止与采集期间退出通过。本机详细证据记录含个人环境信息，留在本地。
- Windows x64：已交叉发布并检查 exe、空模板及 Maa Native 资产；Windows 真机 W1–W3 待验证。
- 自动测试：33 通过、1 个 Windows 原生 API 用例在 Mac 上跳过、0 失败；Release 构建 0 警告、0 错误。包含配置优先级、同名 exe 防误绑、旧帧丢弃、Native 在途停止/释放及停止期间重启保护。
- 远程 CI：每次推送由 [Build and test](https://github.com/xiaogewzw/ArkSide/actions/workflows/build.yml) 在 Mac/Windows 构建、自动测试并打包；发布工作流只接受成功构建中的 Windows ZIP，并校验 Native 资产、附测试清单和 SHA256。实际运行结果以对应提交的 Actions 记录为准。Windows 真机截图/预览仍待 [本轮清单](Windows-B2-测试清单.md) 验收；交叉构建和 ZIP 检查只代表产物检查。

开发包采用 ad-hoc 签名；重建可能使录屏授权失效。若开关显示开启但应用仍报告权限缺失，请先退出程序，在系统录屏列表移除旧条目，再用“+”明确添加固定安装位置的 `GameSidebar-Dev.app`，由用户亲自完成认证并退出重启，以实际目标帧确认授权成功。本轮曾通过此流程恢复真实截图；不要仅按开关状态判定。

如已授权却仍无法截取目标，请复制诊断 JSON，并记录侧边栏显示的实际方法、错误和窗口 ID。当前版本不保证全屏、跨 Spaces、Stage Manager、混合 DPI 或长时运行的兼容性。
