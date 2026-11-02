# CPTools 共通部品の構造

CPToolsはCPClipboard/CPFileLauncher/CPSystemMonitorの製品ディレクトリ、Commonの共通ソース、docs/CPClipboard・docs/CPFileLauncher・docs/CPSystemMonitorの配布サイトとルートのアプリ一覧で構成する。製品名、assembly名、bundle ID、データ保存先、更新確認先と公開サイトURLは以下の製品別公開先を使う。

## 共有の単位

- Common/Core: UI/OS非依存の入力状態機械、画面幾何、ポップアップ判定、汎用JSON保存、色・演出計算。
- Common/Desktop: 同一のAvalonia描画部品、枠・背景・走査線・演出と補助処理。
- Common/Platform: 同一のプラットフォーム抽象。Common/Platform.MacOS・Common/Platform.Windowsは同一のOS実装補助/native定義。OSごとの境界を区別する。
- 共通部品は明示したMSBuildのsource item一覧から、両製品の対応プロジェクトへリンクしてコンパイルする。Commonは製品実行ファイルを持たず、製品間にProjectReferenceを設けない。両アプリの実行状態/メモリを共有するものではない。
- 既存の製品namespace・internal可視性・XAML参照を維持するため、共通ソースの製品namespace/importだけを`CPTOOLS_CLIPBOARD` / `CPTOOLS_LAUNCHER` / `CPTOOLS_MONITOR` の製品別コンパイル定数で切り替える。機能本体へ製品分岐を持ち込まない。定数の未指定/同時指定はビルドエラーとする。共通source itemはCommon自身の場所を基準に解決し、作業ディレクトリへ依存しない。
- product固有enum/設定モデル/EffectCatalog/配色辞書/Strings/画像/フォント/resource URI、アプリの起動設定と盤面、データarchive、更新確認設定は製品が所有する。完全同一の起動用ソースをリンク共有しても、製品名/asset URI/設定・更新先の値は製品別に解決する。差のある処理を無理に1ファイルへ統合しない。共通ソースが参照する既存モデルは各製品側で満たす。
- 共通ソースの元のローカルファイルは同時にCompileしない。共通ファイルの変更は両製品のbuild/testで検証する。net10.0、既存Avalonia/SharpHook/native依存の版とプラットフォーム指定は製品側の整合した設定を使用する。

## ビルド・配布

製品のsolution・publish scriptは各製品のsrc/に置く。rootから呼ぶ場合もCommonを含めて参照を解決する。各製品のbin/releaseへ個別に出力し、既存配布物名・署名identity・IDを保つ。Releaseは同じrepositoryに同居するが、各製品の既存assembly/bundle ID/データを統合しない。

CPTools rootの `CPTools.slnx` は各製品をまとめてbuild/testする入口とする。rootの共通build props/targetsは対応する設定をまとめ、製品固有設定・定数・assetを上書きしない。共通ソース一覧は層別の `.props` または `.projitems` に明示する。

CI定義はCPTools/.github/workflowsに配置し、製品ディレクトリをworking-directoryとして個別solutionを検証する。Common変更時は両方を検証する。更新確認・サイトは公開Release一覧の先頭100件から正式な製品タグを選び、版番号で最新を決める。公開repositoryは `PalmJoy/CPFileLauncher`。Launcherは既存タグ `vX.Y.Z`、Clipboardは `cpclipboard-vX.Y.Z` を使い、Release/asset/更新確認を製品のタグprefixで分離する。各製品のサイトはroot/docs/<product>/に置く。ルートの `https://palmjoy.github.io/CPFileLauncher/` と `/ja/` はCPToolsのアプリ一覧（日英）とし、リダイレクトしない。製品固有の公開URLはそれぞれ `/CPFileLauncher/`・`/CPClipboard/`・`/CPSystemMonitor/` と各製品内の `/ja/`。CPSystemMonitorのReleaseタグprefixは `cpsystemmonitor-v` とし、他製品のReleaseと混ぜない。

署名のlocal pinは各製品work/に保持する。ローカル作業記録・調査・履歴は製品issue/またはwork/へ置き、Commonの仕様へ混在させない。

各製品の配布サイトはdocs/shared/site.cssとsite-ui.jsで配色変数、フォント、見出し・本文、ナビ、ボタン、配布カード、セットアップタブ、レスポンシブ、演出、配色切替と時計を共有する。各製品は盤面模型・説明とRelease選択を持つ。動きを減らすOS設定では走査線の移動・点滅・グリッチを停止する。Clipboardも日英で同じ構造を使い、配色見本は保存せず、そのページ内だけへ反映する。ヘッダーから同じ言語のCPTools一覧へ戻れるようにする。各製品ページの下部に3アプリの正方形リンクセルを静的HTMLで置き、閲覧中の製品を明示する。`Common/build/sync-app-links.mjs` で日英6ページを同期し、`--check` で差分を検証する。背景は文字なしの椰子の木・海・夕景画像、本文やカードはHTMLで実装する。

Clipboard配布サイトの公開済み各OSのZIPリンクはHTMLに常設する。OS判定とトップの直接ダウンロードはRelease APIを待たずに動作し、通信失敗・API制限でも公開済み配布物を取得できる。APIは公開正式版へのリンク・版番号・容量の更新に使う。

CPTools一覧と3製品の日英ページはdocs/shared/analytics.jsを1回読み込み、既存Firebase/GA4プロパティへpage_viewとdownload_clickを送る。content_groupとproductはCPTools・CPFileLauncher・CPClipboard・CPSystemMonitor。page_viewはページURL・タイトル・ページ言語（page_language）を、download_clickはOS（platform）・ページ言語（page_language）・位置（hero/platform_card）・ZIP名・リンク先を含む。初回の自動page_viewは無効にして二重送信を避ける。SDKの読み込み待ちの押下は初期化後に送信し、計測の失敗や非対応はダウンロードを妨げない。CPTools一覧はpage_viewのみ計測し、各製品の配布ページでdownload_clickを計測する。

CPSystemMonitorの公開済みZIPリンクはHTMLに常設し、API通信失敗やJavaScript無効でもダウンロードできる。APIは製品専用の正式公開Releaseを選び、3種類のZIPすべてが正しいGitHubのURLを持つ場合だけリンク・版番号・容量を更新する。通信失敗時も固定済みの公開版リンクとOS別トップリンクを維持する。未公開の段階ではZIP URLを作らず、公開準備中と開発状況へのリンクを表示する。説明・操作例はどちらでも利用可能。標準／コンパクトの表示例は実際の描画エンジンによるサンプルデータのキャプチャを使う。
