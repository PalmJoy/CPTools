#!/bin/zsh
# macOS リリースビルド: bin/osx-arm64/CPSystemMonitor.app を作る。
# 使い方: ./build-release.sh [--zip]   （CODESIGN_IDENTITY=… で署名に使う証明書を指定できる。--zip で bin/release/ に zip も作る）
# ビルドの中身は src/build/publish-mac.sh にある。
set -euo pipefail
cd "${0:A:h}"

if [[ $(uname -s) != Darwin ]]; then
  echo "[build-release] macOS で実行してください（Windows は build-release.bat）" >&2
  exit 1
fi
if pgrep -x CPSystemMonitor > /dev/null; then
  echo "[build-release] CPSystemMonitor が起動中です。終了してから実行してください" >&2
  exit 1
fi

exec src/build/publish-mac.sh "$@"
