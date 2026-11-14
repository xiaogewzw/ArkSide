#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
icon_stage="$(mktemp -d "${TMPDIR:-/tmp}/arkside-icons.XXXXXX")"
trap 'rm -rf "$icon_stage"' EXIT
mkdir -p "$icon_stage/ArkSide.iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" ArkSide.png --out "$icon_stage/ArkSide.iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" ArkSide.png --out "$icon_stage/ArkSide.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$icon_stage/ArkSide.iconset" -o assets/ArkSide.icns
# Vista and later support PNG entries in ICO files; include small sizes for Explorer.
python3 - "$icon_stage" <<'PY'
from pathlib import Path
import struct
import sys
stage = Path(sys.argv[1]) / 'ArkSide.iconset'
images = [(size, (stage / f'icon_{size}x{size}.png').read_bytes()) for size in (16, 32, 128, 256)]
header = struct.pack('<HHH', 0, 1, len(images))
offset = len(header) + 16 * len(images)
entries = []
for size, data in images:
    entries.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
Path('assets/ArkSide.ico').write_bytes(header + b''.join(entries) + b''.join(data for _, data in images))
PY
