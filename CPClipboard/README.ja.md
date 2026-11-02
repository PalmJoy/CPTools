# CPClipboard

[English](README.md)

Windows・macOS向けの定型文ツールです。サイバーパンクの盤面から文章を選び、元のアプリへ貼り付けます。

- **Ctrlを2回**、または **Alt / ⌥ + 中クリック**で呼び出します。設定で変更でき、追加のホットキーは既定でオフです。
- グループは **0〜9の10個**。1グループ500件、1件10,000文字まで登録できます。
- クリック・Enterで貼り付け。**Shift + クリック / Shift + Enter**はコピーだけです。
- 貼り付け後も選んだ定型文がクリップボードに残り、後からCtrl+V / ⌘Vで使えます。元のクリップボードへの復元は任意です（既定OFF、有効時の待ち時間500 ms）。
- 配色・背景画像・演出を調整できます。表示言語は日本語・英語です。

## インストールと初回起動

[公開リリース](https://github.com/PalmJoy/CPTools/releases) · [配布・初回設定](https://palmjoy.github.io/CPTools/CPClipboard/ja/#setup)

配布リンクはRelease公開後に利用できます。.NETは同梱されるため、別途インストールする必要はありません。

| 対象 | 配布zip | 動作環境 |
| --- | --- | --- |
| Windows x64 | `CPClipboard-win-x64.zip` | Windows 10 (1809+) / 11 |
| Windows ARM64 | `CPClipboard-win-arm64.zip` | Windows 10 (1809+) / 11 |
| macOS | `CPClipboard-macos-arm64.zip` | macOS 15以降・Appleシリコン |

**Windows:** zip全体を展開し、`CPClipboard.exe`を起動します。`uiohook.dll`はexeの隣に置いてください。Windowsの警告が出た場合は、入手元を確認してから許可してください。

**macOS:** zipを展開し、`CPClipboard.app`をApplicationsへ移します。「システム設定 → プライバシーとセキュリティ → アクセシビリティ」でCPClipboardを許可します。呼び出しの検知と貼り付けキーの送信に使います。未許可の間はコピーだけになります。未公証の配布物は、入手元を確認したうえで「プライバシーとセキュリティ」から開いてください。メニューバーのメニューから権限ガイドを開けます。

トレイ／メニューバーの「編集…」でグループを選び、文章を追加します。編集は自動保存されます。アプリを終了するときはトレイ／メニューバーのメニューを使います。

## 基本操作

| 操作 | キー |
| --- | --- |
| 定型文を選ぶ | ↑ / ↓ |
| 上から26件を貼り付け / コピーだけ | a〜z / Shift+a〜z |
| 貼り付け | Enter / クリック |
| コピーだけ | Shift + Enter / Shift + クリック |
| グループ切り替え | 0〜9 |
| 前後の空でないグループ | ← / →、Ctrl+Shift+Tab / Ctrl+Tab |
| 先頭・末尾へ | Home / End |
| 盤面を閉じる | Esc |

行の右クリックメニューから貼り付け・コピーだけ・編集を選べます。省略された文章や複数行の文章は、マウスを置くとプレビューできます。「編集…」で本文編集・複製・並べ替え・グループ移動ができ、一覧操作は1段階元に戻せます。

自動貼り付けには有効な貼り付け先と入力の権限が必要です。Windowsの管理者アプリやmacOSのSecure Input中はコピーだけになります。安全に退避できないクリップボード形式や、32 MiBを超える退避データは復元できません。復元前にクリップボードの所有状態を確認しますが、macOSでは他プロセスとの比較・書き込みを不可分にはできません。

「履歴…」でコピーしたテキストを検索・再利用できます。下側のフィルタで検索し、一覧の上からa–zで選択、Shift付きでコピーだけにできます。設定の「履歴」で全消去・分単位の自動消去・最大保存件数（1000件まで）を変更できます。トリガーは編集用・履歴用を別々に設定できます。履歴はPC内の`history.json`へ保存され、バックアップとエクスポートには含まれません。変数やマクロ・リッチテキストの定型文・クラウド同期はありません。更新の確認は公開版を通知して配布ページを開きます。インストールは手動です。

## データとバックアップ

| OS | 保存場所 |
| --- | --- |
| Windows | `%APPDATA%\CPClipboard\` |
| macOS | `~/Library/Application Support/CPClipboard/` |

`settings.json`・`templates.json`・`background/`・`backup/`・任意の`logs/`を保存します。「設定 → データ」からフォルダを開けます。定型文には世代バックアップがあります（既定10世代、10分に1回まで）。手動で控えを取る場合はアプリを終了し、フォルダ全体をコピーしてください。「設定 → データ」でアプリデータのエクスポート／インポートもできます。新しいスキーマのデータは上書きしません。

実行ファイルの隣に`portable.flag`を置くと、その隣の`data/`へ保存します。macOSではアプリバンドル内の実行ファイルが基準です。更新でバンドルを置き換える場合は、ポータブルデータを別途退避してください。

## ビルド

.NET 10 SDKが必要です。macOSでアイコンを生成する場合はQuick Look・ImageMagick・`iconutil`も使います。

```sh
dotnet build src/CPClipboard.sln
dotnet test src/CPClipboard.sln
```

再ビルド前にアプリを終了してください。配布物はWindowsで`build-release.bat -Zip`、macOSで`./build-release.sh --zip`を実行します。Windowsはx64とARM64、macOSはAppleシリコン向けです。zipは`bin/release/`に作成します。Macの署名は`CODESIGN_IDENTITY`、ローカルの`work/macos-signing.identity`、既定の「FileLauncher Dev」の順で指定します。ローカルファイルには有効なコード署名IDのSHA-1フィンガープリントだけを記載します（秘密鍵は保存しません）。証明書がない・一意でない場合はビルドを停止し、署名者を自動変更しません。CIや一時ビルドでは`CODESIGN_IDENTITY=-`でad-hoc署名を明示できます。ad-hoc署名は公証ではありません。追加の署名・公証設定は`src/build/`の配布スクリプトを参照してください。

macOSのアプリはFinderから開くか、`open bin/osx-arm64/CPClipboard.app`でアプリバンドルとして起動してください。アクセシビリティ権限の確認もこの起動方法で行います。

タグ`cpclipboard-v<Version>`（Desktop csprojと一致）からのCIは、両OSでテストし、配布zip・チェックサム・来歴証明・libuiohookソースを添えたReleaseの**下書き**を作ります。公開は手動です。手動のworkflow実行ではArtifactだけを作成します。GitHub Pagesはリポジトリの`docs/`を配信します。

## ライセンス

Copyright © 2026 PalmJoy。[GPL-3.0-only](LICENSE)。同梱ライブラリとフォントは[第三者の表示](THIRD-PARTY-NOTICES.txt)を参照してください。LGPLのネイティブライブラリlibuiohookは、Windowsのexeとは別に差し替えられます。

姉妹アプリ: [CPFileLauncher](https://github.com/PalmJoy/CPTools)

Common を含むCPToolsルートからビルドできます。製品単体を取り出さず、リポジトリ全体を配置してください。ビルド方法は [CPTools](../README.md) を参照してください。
