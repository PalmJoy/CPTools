#!/bin/zsh
# アイコンの SVG（src/CPSystemMonitor.Desktop/Assets/*.svg）から配布物用の画像を作る（macOS で実行。SPEC §3.9 H13）。
#   AppIcon.icns（macOS の .app）/ app.ico（Windows の exe）/ app-256.png（窓のアイコン）/ tray-*.png（トレイ・メニューバー）
# SVG の描画は Quick Look（qlmanage）、透明度の復元は ImageMagick（brew install imagemagick）、.icns は iconutil、.ico は PNG を詰めるだけの小さな Python。
set -euo pipefail
ASSETS=${0:A:h:h}/CPSystemMonitor.Desktop/Assets
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

ql() { # ql <svg> <px> <out.png>
  qlmanage -t -s "$2" -o "$TMP" "$1" > /dev/null 2>&1
  mv "$TMP/$(basename "$1").png" "$3"
  sips -z "$2" "$2" "$3" > /dev/null # Quick Look は 1 px ずれることがあるので大きさをそろえる
}

# Quick Look は透明の所を白で塗るので、白地と黒地の 2 回描いて差から透明度を戻す（白地 W・黒地 B のとき α = 1 - (W - B)、色 = B / α）。
# ImageMagick 内蔵の SVG 描画はぼかし・線の一部を描けないので描画には使わず、合成だけに使う。
render() { # render <svg> <px> <out.png>
  mkdir -p "$TMP/black"
  perl -0pe 's/(<svg\b[^>]*>)/$1<rect x="-100000" y="-100000" width="200000" height="200000" fill="#000"\/>/' "$1" > "$TMP/black/$(basename "$1")"
  ql "$1" "$2" "$TMP/w.png"
  ql "$TMP/black/$(basename "$1")" "$2" "$TMP/b.png"
  magick "$TMP/b.png" "$TMP/w.png" -colorspace sRGB -fx "1-((v.r-u.r)+(v.g-u.g)+(v.b-u.b))/3" -channel R -separate +channel "$TMP/a.png"
  magick "$TMP/b.png" "$TMP/a.png" -fx "v.r<0.004 ? 0 : min(1, u/v.r)" "$TMP/c.png"
  magick "$TMP/c.png" "$TMP/a.png" -alpha off -compose CopyOpacity -composite -define png:color-type=6 "$3"
}

echo "==> アプリアイコン"
ICONSET=$TMP/AppIcon.iconset
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  render "$ASSETS/icon.svg" $s "$ICONSET/icon_${s}x${s}.png"
  render "$ASSETS/icon.svg" $((s * 2)) "$ICONSET/icon_${s}x${s}@2x.png"
done
iconutil -c icns "$ICONSET" -o "$ASSETS/AppIcon.icns"
cp "$ICONSET/icon_256x256.png" "$ASSETS/app-256.png"

mkdir -p "$TMP/ico"
for s in 16 24 32 48 64 128 256; do render "$ASSETS/icon.svg" $s "$TMP/ico/$s.png"; done
python3 - "$TMP/ico" "$ASSETS/app.ico" <<'PY'
import struct, sys, os
d, out = sys.argv[1], sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
blobs = [open(os.path.join(d, f"{s}.png"), "rb").read() for s in sizes]
head = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
entries = b""
for s, b in zip(sizes, blobs):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(b), offset)
    offset += len(b)
open(out, "wb").write(head + entries + b"".join(blobs))
PY

echo "==> トレイ / メニューバー"
# Tray SVGs contain only simple paths. Render directly on transparency:
# Quick Look's thumbnail background/shadow contaminates the recovered alpha,
# making macOS template icons appear as solid squares.
# Use the drawing API because ImageMagick's SVG delegate varies by machine.
python3 - "$ASSETS" <<'PY'
import pathlib, subprocess, sys, xml.etree.ElementTree as ET
assets = pathlib.Path(sys.argv[1])
for name in ('tray-template', 'tray-color', 'tray-warning'):
    path, = ET.parse(assets / f'{name}.svg').getroot()
    subprocess.run([
        'magick', '-size', '128x128', 'xc:none', '-fill', 'none',
        '-stroke', path.attrib['stroke'], '-strokewidth', path.attrib['stroke-width'],
        '-draw', f"scale 4,4 stroke-linejoin round path '{path.attrib['d']}'",
        '-resize', '32x32', '-define', 'png:color-type=6', str(assets / f'{name}.png')
    ], check=True)
PY
ls -la "$ASSETS"/*.icns "$ASSETS"/*.ico "$ASSETS"/*.png
