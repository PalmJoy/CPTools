# CPSystemMonitor

[配布ページ（日本語）](https://palmjoy.github.io/CPFileLauncher/CPSystemMonitor/ja/) / [English](https://palmjoy.github.io/CPFileLauncher/CPSystemMonitor/)

CPToolsシリーズのWindows・macOS向け常駐システムモニターです。CPU・メモリ・ストレージ・ネットワークを、シリーズ共通の配色とHUD枠で表示します。

- 標準／コンパクト表示。右クリックで切り替え。
- CPU全体と論理CPU別の負荷。標準は横4列、コンパクトは幅340 DIP・横12列で、CPU数に合わせて段数を変更。
- 利用可能メモリの割合と容量、選択したボリュームの空き割合と容量。
- ディスク読み書き速度、稼働中インターフェースの送受信速度。
- 取得可能なプロセスをアプリ単位にまとめた最大負荷対象。CPUは全体を100%として計算。
- 各欄・各欄の最大負荷アプリ・各欄の履歴グラフと、コア別使用率を個別に表示切り替え。
- 現在日時（既定は `yyyy/MM/dd HH:mm`）。表示モード・位置・最前面・表示設定を保存。
- 更新間隔は初期値1秒、履歴期間は初期値60秒。設定で変更可能。
- ×でトレイへ格納。終了はメニューから。自動起動は初期値オフ。
- 配色・背景・演出・トリガー・表示位置・言語・自動起動・データ管理は既存シリーズと同じ設定操作。

## 実行・ビルド

.NET 10 SDKが必要です。リポジトリ全体を配置して、CPToolsルートから実行します。

```sh
dotnet restore CPTools.slnx --locked-mode
dotnet run --project CPSystemMonitor/src/CPSystemMonitor.Desktop/CPSystemMonitor.Desktop.csproj
```

macOSアプリの作成は `CPSystemMonitor/build-release.sh`、Windowsは `CPSystemMonitor/build-release.bat`。macOSの署名は既存シリーズと同じ指定規則で、製品別の `work/macos-signing.identity` または `CODESIGN_IDENTITY` を使用します。開発用ad-hoc署名は明示的に `CODESIGN_IDENTITY=-` を指定してください。

## 取得範囲と権限

通常の使用率と容量の取得に、センサー用の追加ドライバーは使用しません。温度・クロック・GPUなど、両OSの共通項目として保証できない値は表示しません。

設定の「詳細」にアプリ別詳細計測と権限の説明があります。初期値はオフです。Windowsのディスク・通信のアプリ別計測はOSのETWを使用し、ユーザーが権限要求を選んだときだけ、確認後に管理者として再起動します。macOSは標準プロセス統計と `nettop` を利用します。読み取れないプロセスは除外し、取得できない最大負荷アプリ行は表示しません。負荷がゼロのときも行を隠します。

容量は選択したボリュームを表示します。ディスクI/OはWindowsでは選択ボリューム、macOSでは対応する物理ディスク（APFSでは他ボリュームと共有）を測ります。**ストレージの最大負荷アプリは全ディスク集計**で、行の `ALL DISKS` とツールチップで区別します。OS標準のプロセス統計だけでは、macOSでプロセスのI/Oをボリューム別に分離できないためです。

通信量は、稼働中のループバック・トンネル以外のインターフェースの合計です。VPN・ブリッジなどでは同じ通信が複数の接続に現れる場合があります。アプリ別の通信量とインターフェース合計は、計測層が異なるため一致するとは限りません。

メモリ利用可能量はWindowsではOSのAvailable Physical Memory、macOSではfree+inactiveページを使用します。アプリ別使用量はWindowsではワーキングセット、macOSではphysical footprintを集計します。キャッシュ・圧縮・共有の扱いが異なり、OS間の完全な同値や全体との一致は保証しません。表示のB/KB/MB/GB/TBは1024単位です。

## 検証

```sh
dotnet test CPTools.slnx --no-restore
node Common/build/test.mjs
# 現在のOSの実データ取得を確認（通常のユーザーとして実行）
CPSM_NATIVE_TESTS=1 dotnet test CPSystemMonitor/src/CPSystemMonitor.Tests/CPSystemMonitor.Tests.csproj
# 画面の描画キャプチャ
CPSM_CAPTURE_DIR=/absolute/path/to/captures dotnet test CPSystemMonitor/src/CPSystemMonitor.Desktop.Tests/CPSystemMonitor.Desktop.Tests.csproj
```

詳細は [仕様](spec/SPEC.md)。GPL-3.0-only、第三者ライセンスは [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

## リリース

`cpsystemmonitor-v<Version>` の注釈付きタグで `.github/workflows/release-systemmonitor.yml` を実行します。Windows・macOSのテスト、実データ取得、ZIP作成がすべて成功してからReleaseを作成します。タグ注釈に `Release-Publish: true` がある場合は正式公開し、それ以外は下書きにします。手動実行はArtifact作成のみです。macOSは既存シリーズと同じad-hoc署名です。
