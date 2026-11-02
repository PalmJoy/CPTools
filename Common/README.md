# Common

各アプリが同じ実ファイルを使用する共通ソースです。`SharedItems.props` が製品と層を選び、既存assemblyにリンクしてコンパイルします。製品固有の設定・リソース・盤面処理は製品ディレクトリに置きます。

- `Core/`: 入力判定・幾何・保存・色・演出の計算。
- `Desktop/`: Avaloniaの枠・背景・HUD・演出・補助処理。
- `Platform*/`: OS境界ごとの共通補助。
- `Web/`: 製品別Releaseの選択。
- `build/`: 固定署名の選択、サイト同期、構成検証。
- `Tests/`: 両製品で実行する共通の検証。

C#の共有対象は `shared-sources.json`、コンパイル条件は `SharedItems.props`、全体の仕様は [SPEC.md](SPEC.md) を参照してください。変更後はCPToolsルートのビルドで両製品を検証します。

## 配布サイトのAnalytics

既存のFirebase/GA4プロパティ（G-2VWQWZKZC4）で両製品を計測します。GA4の「ページとスクリーン」ではページパスまたは「コンテンツグループ」（CPFileLauncher / CPClipboard）でアクセスを分けます。DLボタン押下は既存の `download_click` イベントです。

`download_click`には `content_group`・`product`・`platform`（win-x64 / win-arm64 / macos-arm64）・`page_language`（ページ言語）・`placement`（hero / platform_card）・`file_name`・`link_url`を付けます。独自パラメータを通常のレポートや探索で使う場合は、GA4管理画面のカスタム定義で必要な項目をイベントスコープのカスタムディメンションとして登録します。登録済みの同名項目はそのまま使えます。計測はページ表示・ボタン押下であり、ダウンロード完了の計測ではありません。
