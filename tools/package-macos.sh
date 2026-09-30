#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
app_path="${1:-artifacts/GameSidebar-Dev.app}"
mkdir -p "$app_path/Contents/MacOS"
dotnet restore src/GameSidebar.App/GameSidebar.App.csproj -p:RuntimeIdentifier=osx-arm64 --locked-mode
dotnet publish src/GameSidebar.App/GameSidebar.App.csproj -c Release -r osx-arm64 --self-contained true --no-restore -o "$app_path/Contents/MacOS"
cat > "$app_path/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>dev.arkside.GameSidebar</string>
  <key>CFBundleName</key><string>GameSidebar Dev</string>
  <key>CFBundleDisplayName</key><string>GameSidebar Dev</string>
  <key>CFBundleExecutable</key><string>GameSidebar.App</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --deep --sign - "$app_path"
echo "$app_path"
