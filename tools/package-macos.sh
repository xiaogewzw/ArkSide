#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
app_path="${1:-artifacts/release/ArkSide.app}"
recognition_python="${ARKSIDE_RECOGNITION_BUILD_PYTHON:-.venv-recognition/bin/python}"
if [[ ! -x "$recognition_python" ]]; then
  echo "缺少识别组件构建环境，请创建 .venv-recognition 并安装 tools/offline_recognition/requirements-build.txt" >&2
  exit 1
fi
if [[ "${app_path:t}" != "ArkSide.app" ]]; then
  echo "正式应用包名称必须是 ArkSide.app" >&2
  exit 1
fi
# Always build in a clean staging directory so development files cannot leak into the app.
mkdir -p "${app_path:h}"
package_stage="$(mktemp -d "${app_path:h}/.arkside-package.XXXXXX")"
trap 'rm -rf "$package_stage"' EXIT
staged_app="$package_stage/ArkSide.app"
mkdir -p "$staged_app/Contents/MacOS" "$staged_app/Contents/Resources"
tools/prepare-icons.sh
dotnet restore src/GameSidebar.App/GameSidebar.App.csproj -p:RuntimeIdentifier=osx-arm64 -p:SelfContained=true --locked-mode
dotnet publish src/GameSidebar.App/GameSidebar.App.csproj -c Release -r osx-arm64 --self-contained true --no-restore -p:ArkSideProduction=true -p:DebugType=None -o "$staged_app/Contents/MacOS"
# Windows native assets are not needed by this macOS product.
rm -rf "$staged_app/Contents/MacOS/runtimes/win-x64"
"$recognition_python" -m tools.offline_recognition.build_worker --output "$staged_app/Contents/Resources/recognition/worker"
cp assets/ArkSide.icns "$staged_app/Contents/Resources/ArkSide.icns"
cat > "$staged_app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.arkside.app</string>
  <key>CFBundleName</key><string>ArkSide</string>
  <key>CFBundleDisplayName</key><string>ArkSide</string>
  <key>CFBundleExecutable</key><string>ArkSide</string>
  <key>CFBundleIconFile</key><string>ArkSide.icns</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
find "$staged_app/Contents/MacOS" -name '*.pdb' -delete
codesign --force --deep --sign - "$staged_app"
codesign --verify --deep --strict "$staged_app"
# Only replace this generated product after the entire new bundle is built and signed.
if [[ -e "$app_path" ]]; then
  mv "$app_path" "$package_stage/previous-ArkSide.app"
fi
mv "$staged_app" "$app_path"
archive="${app_path:h}/ArkSide-macOS-arm64.zip"
ditto -c -k --sequesterRsrc --keepParent "$app_path" "$archive"
(cd "${app_path:h}" && shasum -a 256 ArkSide-macOS-arm64.zip > ArkSide-macOS-arm64.zip.sha256)
echo "$app_path"
echo "$archive"
