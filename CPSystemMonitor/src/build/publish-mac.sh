#!/bin/zsh
# macOS 配布物 bin/osx-arm64/CPSystemMonitor.app を作る（SPEC §10.4）。
# 使い方: src/build/publish-mac.sh [--zip]   （CODESIGN_IDENTITY=… で署名に使う証明書を指定できる。- で ad-hoc）
#   --zip: bin/release/CPSystemMonitor-macos-arm64.zip も作る（GitHub Releases のアセット。SPEC §10.4「リリース」）
set -euo pipefail

ZIP=0
for arg in "$@"; do
  case $arg in
    --zip) ZIP=1 ;;
    *) echo "使い方: $0 [--zip]" >&2; exit 2 ;;
  esac
done

if pgrep -x CPSystemMonitor > /dev/null; then
  echo "CPSystemMonitor が起動中です。終了してから実行してください" >&2
  exit 1
fi

RID=osx-arm64                       # Apple シリコン専用（SPEC §13.2 T7）
BUNDLE_ID=com.palmjoy.cpsystemmonitor  # SPEC §13.2 T6（2026-10-04 確定）。macos/Info.plist と合わせる
EXE=CPSystemMonitor                   # 実行ファイル名（csproj の AssemblyName）

ROOT=${0:A:h:h:h}
PROJECT=$ROOT/src/CPSystemMonitor.Desktop
APP=$ROOT/bin/$RID/$EXE.app
source "$ROOT/src/build/resolve-mac-signing.sh"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT/CPSystemMonitor.Desktop.csproj" | head -1)
VERSION=${VERSION:-0.0.0}

echo "==> dotnet publish ($RID, Release, self-contained, ReadyToRun) version $VERSION"
# ReadyToRun（事前コンパイル）: 起動が約 0.2 秒速くなる。配布物は約 +15 MB（2026-10-04 ユーザー判断。SPEC §10.4 / §11）
dotnet publish "$PROJECT/CPSystemMonitor.Desktop.csproj" -c Release -r $RID --self-contained -p:UseAppHost=true -p:PublishReadyToRun=true -o "$TMP/publish" -nologo -v q

echo "==> $APP を組み立て"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
sed "s/__VERSION__/$VERSION/g" "$PROJECT/macos/Info.plist" > "$APP/Contents/Info.plist"
cp "$PROJECT/Assets/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns" # アイコン（src/build/make-icons.sh で作る）
cp -R "$TMP/publish/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/$EXE"
cp -R "$PROJECT/macos/"*.lproj "$APP/Contents/Resources/" # 許可ダイアログの文言の各言語版（InfoPlist.strings）
plutil -lint "$APP/Contents/Info.plist" "$APP/Contents/Resources/"*.lproj/InfoPlist.strings > /dev/null
# ライセンス文（GPL の写しと第三者の表示。SPEC §10.4「ライセンス文の同梱」）。署名の前に置く（後から足すと封印が壊れる）
cp "$ROOT/LICENSE" "$APP/Contents/Resources/LICENSE.txt"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$APP/Contents/Resources/THIRD-PARTY-NOTICES.txt"

# 同じ証明書とbundle idで署名し、更新時のアプリ識別条件を維持する。
echo "==> 署名: $IDENTITY"
# Contents/MacOS はDLLやJSONもコードとして検証されるので、全ファイルを署名する。
# --deep には頼らず、中身 → バンドルの順。本体の実行ファイル（CFBundleExecutable）はバンドルの署名に含まれるので最後
find "$APP/Contents/MacOS" -type f ! -path "$APP/Contents/MacOS/$EXE" | while read -r f; do
  codesign --force "${SIGN_OPTIONS[@]}" --sign "$IDENTITY" "$f"
done
codesign --force "${SIGN_OPTIONS[@]}" --entitlements "$PROJECT/macos/entitlements.plist" --sign "$IDENTITY" --identifier "$BUNDLE_ID" "$APP"
codesign --verify --deep --strict "$APP"
if [[ "$IDENTITY" != '-' ]]; then
  codesign --verify --strict -R "=certificate leaf = H\"$IDENTITY\"" "$APP"
fi

echo "==> 確認"
file "$APP/Contents/MacOS/$EXE"
du -sh "$APP"
echo "完了: $APP"

if [[ -n "${NOTARYTOOL_PROFILE:-}" ]]; then
  ditto -c -k --keepParent "$APP" "$TMP/notarize.zip"
  xcrun notarytool submit "$TMP/notarize.zip" --keychain-profile "$NOTARYTOOL_PROFILE" --wait
  xcrun stapler staple "$APP"
  xcrun stapler validate "$APP"
  spctl --assess --type execute --verbose "$APP"
fi

if (( ZIP )); then
  # ditto で固める（シンボリックリンク・拡張属性 = .dll などの署名を壊さない。zip -r は使わない）
  ZIPFILE=$ROOT/bin/release/$EXE-${RID/osx-/macos-}.zip
  mkdir -p "$ROOT/bin/release"
  rm -f "$ZIPFILE"
  ditto -c -k --keepParent "$APP" "$ZIPFILE"
  # 展開し直して署名が保たれていることを確かめる（SPEC §13.3 C31）
  ditto -x -k "$ZIPFILE" "$TMP/verify"
  codesign --verify --deep --strict "$TMP/verify/$EXE.app"
  echo "zip: $ZIPFILE  $(du -h "$ZIPFILE" | cut -f1)"
fi
