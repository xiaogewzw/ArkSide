#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
app_path="${1:-artifacts/release/ArkSide.app}"
recognition_python="${ARKSIDE_RECOGNITION_BUILD_PYTHON:-.venv-recognition/bin/python}"
if [[ "$(uname -m)" != "arm64" ]]; then
  echo "macOS arm64 应用和离线识别组件必须在 Apple Silicon 上构建。" >&2
  exit 1
fi
if [[ ! -x "$recognition_python" ]]; then
  echo "缺少识别组件构建环境，请创建 .venv-recognition 并安装 tools/offline_recognition/requirements-build.txt" >&2
  exit 1
fi
app_version="$("$recognition_python" tools/release_metadata.py version)"
bundle_version="${app_version%%-*}"
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
cp THIRD_PARTY_NOTICES.md "$staged_app/Contents/Resources/THIRD_PARTY_NOTICES.md"
cp README.md "$staged_app/Contents/Resources/README.md"
"$recognition_python" tools/release_metadata.py manifest --platform macOS-arm64 --output "$staged_app/Contents/Resources/release.json"
cat > "$staged_app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.arkside.app</string>
  <key>CFBundleName</key><string>ArkSide</string>
  <key>CFBundleDisplayName</key><string>ArkSide</string>
  <key>CFBundleExecutable</key><string>ArkSide</string>
  <key>CFBundleIconFile</key><string>ArkSide.icns</string>
  <key>CFBundleShortVersionString</key><string>$bundle_version</string>
  <key>CFBundleVersion</key><string>$bundle_version</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
find "$staged_app/Contents/MacOS" -name '*.pdb' -delete
codesign --force --deep --sign - "$staged_app"
codesign --verify --deep --strict "$staged_app"
# Verify the staged product before replacing a previously working local app.
"$recognition_python" -m tools.offline_recognition.verify_packaged --app-dir "$staged_app/Contents/MacOS" --output-dir artifacts/release-checks/macos-worker
published_app="${staged_app:A}/Contents/MacOS"
dotnet run --project tools/GameSidebar.RecognitionCheck/GameSidebar.RecognitionCheck.csproj -c Release "-p:RecognitionAppDirectory=$published_app" -- "$published_app" artifacts/release-checks/macos-service.json \
  '干员数据/识别测试/测试截图.png' '干员数据/识别测试/识别结果.json' \
  '干员数据/识别测试/游戏内截图-02.png' '干员数据/识别测试/识别结果-02.json' \
  '干员数据/识别测试/游戏内截图-03.png' '干员数据/识别测试/识别结果-03.json'
codesign --verify --deep --strict "$staged_app"
# Only replace this generated product after the entire new bundle is built and signed.
if [[ -e "$app_path" ]]; then
  mv "$app_path" "$package_stage/previous-ArkSide.app"
fi
mv "$staged_app" "$app_path"
archive="${app_path:h}/ArkSide-v${app_version}-macOS-arm64.zip"
ditto -c -k --sequesterRsrc --keepParent "$app_path" "$archive"
(cd "${app_path:h}" && shasum -a 256 "${archive:t}" > "${archive:t}.sha256")
cp "$app_path/Contents/Resources/release.json" "${app_path:h}/macOS-arm64-release.json"
echo "$app_path"
echo "$archive"
