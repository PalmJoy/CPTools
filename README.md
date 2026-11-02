# CPTools

CPFileLauncher・CPClipboard・CPSystemMonitorをまとめたWindows・macOS向けツール集です。公開リポジトリは既存の[CPFileLauncher](https://github.com/PalmJoy/CPFileLauncher)を使用します。

| 製品 | 用途 | 配布サイト |
| --- | --- | --- |
| [CPFileLauncher](CPFileLauncher/README.md) | ファイル・フォルダ・アプリの起動 | [日本語](https://palmjoy.github.io/CPFileLauncher/CPFileLauncher/ja/) / [English](https://palmjoy.github.io/CPFileLauncher/CPFileLauncher/) |
| [CPClipboard](CPClipboard/README.ja.md) | 定型文のコピー・貼り付け | [日本語](https://palmjoy.github.io/CPFileLauncher/CPClipboard/ja/) / [English](https://palmjoy.github.io/CPFileLauncher/CPClipboard/) |
| [CPSystemMonitor](CPSystemMonitor/README.md) | CPU・メモリ・ディスク・通信の常駐監視 | [日本語](https://palmjoy.github.io/CPFileLauncher/CPSystemMonitor/ja/) / [English](https://palmjoy.github.io/CPFileLauncher/CPSystemMonitor/) |

各製品は別アプリです。設定・データ保存先・bundle ID・配布ファイル名を個別に持ちます。

## 構成

- `CPFileLauncher/`・`CPClipboard/`・`CPSystemMonitor/`: 製品ごとのソース、設定、テスト、配布スクリプト。
- `Common/`: 入力判定、保存、演出、画面枠、OS補助、署名選択、更新確認などの共通ソース。
- `docs/index.html`・`docs/ja/index.html`: CPToolsのアプリ一覧（日英）。
- `docs/CPFileLauncher/`・`docs/CPClipboard/`・`docs/CPSystemMonitor/`: 各配布ページ。
- `CPTools.slnx`: 各製品をまとめてビルド・テストする入口。

CommonのC#ソースは各製品へリンクしてコンパイルします。アプリの名前空間・リソース・型の互換を保ち、片方のアプリをもう片方から参照する構成にはしません。[共通部品の仕様](Common/SPEC.md)を参照してください。

## ビルドとテスト

.NET 10 SDKとNode.jsが必要です。CPToolsルートで実行します。

```sh
./build.sh
```

Windowsでは `./build.ps1` を使用します。個別に実行する場合:

```sh
dotnet restore CPTools.slnx --locked-mode
dotnet build CPTools.slnx -c Release --no-restore
dotnet test CPTools.slnx -c Release --no-build --no-restore
node Common/build/test.mjs
```

製品ごとの既存solutionも利用できます。Commonとルートのbuild設定が必要なので、リポジトリ全体を配置してください。配布ビルドはそれぞれの製品ディレクトリから `build-release.sh --zip`（macOS）または `build-release.bat -Zip`（Windows）を実行します。出力は製品ごとの `bin/release/` です。再ビルド前に対象アプリを終了してください。

Macの署名pinは製品ごとの `work/macos-signing.identity` に置きます。明示的な `CODESIGN_IDENTITY` を優先し、利用できない証明書から別署名へ自動変更しません。アプリはFinderまたは `open <appのパス>` で起動します。

## 配布サイトとRelease

ルートはCPToolsのアプリ一覧、Launcherの製品ページは `CPFileLauncher/` 配下です。ダウンロードファイル名・`vX.Y.Z`タグは維持します。Clipboardは同じrepositoryに `cpclipboard-vX.Y.Z`タグでReleaseを作り、Launcherのlatest指定を置き換えません。更新確認は製品タグで分離します。CIの手動実行はArtifactを作成します。Launcherの注釈付きタグによる公開指定は既存の運用に従い、Clipboardも注釈付きタグに単独行 `Release-Publish: true` を指定した場合はテスト・ビルド後に公開し、それ以外は下書きReleaseを作成します。

Launcherのサイトは `docs/CPFileLauncher/` を編集後、次のコマンドで共通Web部品を同期します。

```sh
node Common/build/sync-launcher-site.mjs
node Common/build/sync-launcher-site.mjs --check
node Common/build/sync-app-links.mjs
node Common/build/sync-app-links.mjs --check
```

GitHub Pagesの配信元はrootの `docs/` です。`docs/index.html` と `docs/ja/index.html` は3アプリを選べるCPToolsの一覧です。各配布ページは製品ディレクトリ内に置き、椰子の木の背景・共通ナビ・正方形のアプリリンクを `docs/shared/` と共通同期スクリプトで揃えます。CPSystemMonitorは専用の `cpsystemmonitor-vX.Y.Z` 公開Releaseに3種類のZIPが揃うとダウンロードを有効にし、未公開時は公開準備中と開発状況リンク、取得失敗時はその説明とRelease一覧へのリンクを表示します。

## ライセンス

Copyright © 2026 PalmJoy。[GPL-3.0-only](LICENSE)。各製品の第三者表示は[Launcher](CPFileLauncher/THIRD-PARTY-NOTICES.txt)・[Clipboard](CPClipboard/THIRD-PARTY-NOTICES.txt)・[SystemMonitor](CPSystemMonitor/THIRD-PARTY-NOTICES.txt)を参照してください。
