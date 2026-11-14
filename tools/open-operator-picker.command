#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
# Screenshot import is a source-only development tool.
exec dotnet run --project src/GameSidebar.App -c Release -- --operator-picker "$@"
