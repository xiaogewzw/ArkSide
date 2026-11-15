param([string]$OutputDirectory = "artifacts/release/windows/ArkSide")
$ErrorActionPreference = "Stop"
$env:PYTHONUTF8 = '1'
Set-Location (Join-Path $PSScriptRoot "..")
if (![System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw "Windows 离线识别组件必须在 Windows 上构建。"
}
if (Test-Path $OutputDirectory) { throw "输出目录已存在，请选择新的空目录：$OutputDirectory" }
function Assert-Success { if ($LASTEXITCODE -ne 0) { throw "构建命令失败，退出码 $LASTEXITCODE" } }
$appVersion = python tools/release_metadata.py version
Assert-Success
dotnet restore src/GameSidebar.App/GameSidebar.App.csproj -p:RuntimeIdentifier=win-x64 -p:SelfContained=true --locked-mode
Assert-Success
dotnet publish src/GameSidebar.App/GameSidebar.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:ArkSideProduction=true -p:DebugType=None -o $OutputDirectory
Assert-Success
python -m tools.offline_recognition.build_worker --output "$OutputDirectory/recognition/worker"
Assert-Success
Get-ChildItem $OutputDirectory -Recurse -Filter *.pdb | Remove-Item
$macNative = Join-Path $OutputDirectory 'runtimes/osx-arm64'
if (Test-Path $macNative) { Remove-Item $macNative -Recurse -Force }
foreach ($required in @('ArkSide.exe', 'ArkSide.dll', 'recognition/worker/ArkSide.Recognition.exe', 'recognition/data/干员详细信息.json', 'recognition/data/干员禁用规则.json', 'runtimes/win-x64/native/MaaFramework.dll', 'runtimes/win-x64/native/MaaWin32ControlUnit.dll')) {
    if (!(Test-Path (Join-Path $OutputDirectory $required))) { throw "缺少发布文件：$required" }
}
foreach ($excluded in @('GameSidebar.App.exe', 'recognition/python', 'target-game.example.json')) {
    if (Test-Path (Join-Path $OutputDirectory $excluded)) { throw "发现开发文件：$excluded" }
}
Copy-Item 'THIRD_PARTY_NOTICES.md' (Join-Path $OutputDirectory 'THIRD_PARTY_NOTICES.md')
Copy-Item 'README.md' (Join-Path $OutputDirectory 'README.md')
python tools/release_metadata.py manifest --platform Windows-x64 --output "$OutputDirectory/release.json"
Assert-Success
python -m tools.offline_recognition.verify_packaged --app-dir $OutputDirectory --output-dir artifacts/release-checks/windows-worker
Assert-Success
$publishedApp = (Resolve-Path $OutputDirectory).Path
dotnet run --project tools/GameSidebar.RecognitionCheck/GameSidebar.RecognitionCheck.csproj -c Release "-p:RecognitionAppDirectory=$publishedApp" -- $publishedApp artifacts/release-checks/windows-service.json '干员数据/识别测试/测试截图.png' '干员数据/识别测试/识别结果.json' '干员数据/识别测试/游戏内截图-02.png' '干员数据/识别测试/识别结果-02.json' '干员数据/识别测试/游戏内截图-03.png' '干员数据/识别测试/识别结果-03.json'
Assert-Success
python -m tools.offline_recognition.verify_external_data --app-dir $publishedApp --output-dir artifacts/release-checks/windows-external-data
Assert-Success
# Exercise startup using the self-contained executable, without dotnet or Python launchers.
$appProcess = Start-Process -FilePath (Join-Path $publishedApp 'ArkSide.exe') -WorkingDirectory $env:TEMP -PassThru
try {
    Start-Sleep -Seconds 5
    $appProcess.Refresh()
    if ($appProcess.HasExited) { throw "ArkSide 启动后退出，退出码 $($appProcess.ExitCode)" }
    $startup = @{ passed = $true; executable = 'ArkSide.exe'; self_contained = $true }
    $startup | ConvertTo-Json | Set-Content artifacts/release-checks/windows-startup.json -Encoding utf8
}
finally {
    $appProcess.Refresh()
    if (!$appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (!$appProcess.WaitForExit(10000)) { $appProcess.Kill(); $appProcess.WaitForExit() }
    }
}
$archive = Join-Path (Split-Path $OutputDirectory -Parent) "ArkSide-v$appVersion-Windows-x64.zip"
Compress-Archive -Path $OutputDirectory -DestinationPath $archive -Force
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content "$archive.sha256" -Encoding ascii
Copy-Item (Join-Path $OutputDirectory 'release.json') (Join-Path (Split-Path $OutputDirectory -Parent) 'Windows-x64-release.json')
Write-Output $archive
