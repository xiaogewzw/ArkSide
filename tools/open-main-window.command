#!/bin/zsh
set -euo pipefail
project_dir="${0:A:h:h}"
app_bundle="$project_dir/artifacts/release/ArkSide.app"
if [[ -x "$app_bundle/Contents/MacOS/ArkSide" ]]; then
  exec open -n "$app_bundle" --args "$@"
fi
cd "$project_dir"
exec dotnet run --project src/GameSidebar.App -c Release -- "$@"
