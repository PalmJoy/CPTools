# CPFileLauncher

ホットキーやマウスで呼び出す、ネオンの盤面のファイル / アプリランチャー（Windows / macOS）。
A cyberpunk file & app launcher for Windows and macOS, summoned with a hotkey or a mouse click.

- 配布サイト / Website: [Windows・macOS 用の無料ファイルランチャー](https://palmjoy.github.io/CPFileLauncher/CPFileLauncher/ja/) / [Free file launcher for Windows & macOS](https://palmjoy.github.io/CPFileLauncher/CPFileLauncher/)
- ダウンロード / Download: [最新のリリース / Latest release](https://github.com/PalmJoy/CPFileLauncher/releases/latest)（Windows x64 / ARM64、macOS Apple シリコン）
- ライセンス / License: GPL-3.0-only（[LICENSE](LICENSE)）。Copyright © 2026 PalmJoy。第三者の部品 / Third-party components: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)

「設定 → 呼び出し」で修飾キーの2回押しを有効にできます（既定OFF・Ctrl・300ms）。他のアプリと同じ操作を設定すると両方が反応するため、CPClipboardと併用する場合はキーを分けてください。
Enable modifier double-tap in Settings → Triggers (off by default; Ctrl, 300 ms). Choose a different modifier from CPClipboard when running both apps.

## 安全な利用 / Safe use

出所不明の設定ZIPは取り込まないでください。盤面の項目には、コマンドやネットワーク上の場所を登録できます。
Only import settings archives from trusted sources: board items can run commands and open network locations.

## 配布ビルド / Release builds

- Self-contained配布は.NET 10.0.13以上が必要です。依存関係は`packages.lock.json`で固定し、CIでは固定モードで復元します。既知のNuGet脆弱性はビルドエラーになります。
- macOS: 署名IDは`CODESIGN_IDENTITY`、ローカルの`work/macos-signing.identity`（SHA-1フィンガープリント）、既定名`FileLauncher Dev`の順で選びます。証明書が見つからない・一意でない場合は停止し、別証明書やad-hocに自動変更しません。`CODESIGN_IDENTITY=-`はCI・一時ビルド用の明示指定です。`CODESIGN_IDENTITY`に`Developer ID Application: …`を指定するとHardened Runtimeとタイムスタンプを使います。`NOTARYTOOL_PROFILE`にキーチェーンの公証プロファイル名を指定すると、公証とstapleも実施します。証明書や認証情報はリポジトリに保存しません。
- Windows: `CPFL_SIGNING_THUMBPRINT`（または`publish-win.ps1 -SigningThumbprint`）にCurrentUserの証明書ストアのコード署名証明書を指定すると、exeとuiohook.dllをSHA-256とタイムスタンプ付きで署名します。
- Windowsで証明書を指定しないビルドは未署名です。macOSのCIはad-hoc署名を明示します。CIのRelease ZIPにはGitHubの来歴証明を付けます。ダウンロード後は`gh attestation verify <ZIPのパス> --repo PalmJoy/CPFileLauncher`で検証できます。

注釈付きタグのメッセージに単独行`Release-Publish: true`を含めると、両OSのテストと配布物の作成が成功した後にReleaseを公開します。指定しないタグは下書きになります。

macOSはFinder、または`open bin/osx-arm64/CPFileLauncher.app`でアプリとして起動してください。アクセシビリティ権限の確認もこの方法で行います。

Commonを含むリポジトリ全体からビルドします。統合ビルドと構成は [CPTools](../README.md) を参照してください。既存の公開URLとLauncherのバージョンタグは維持しています。
