# Arcdini 実装状況

更新日 2026-11-23

参照会話「Filedini性能改善案」に基づき、独立した .NET ライブラリと Windows x64 用の NuGet を作る。Filedini への組み込みは後続作業。

- [x] 参照会話と既存リポジトリを確認。既存のコードはなく、MIT ライセンスが設定済み。
- [x] RAR・RAR5 を除いた libarchive をビルド。DLL の依存先は bcrypt.dll と KERNEL32.dll。RAR 用のソースとエクスポートがなく、DLL と通知文を `Arcdini.0.1.0.nupkg` に同梱したことを確認。
- [x] 一覧、全体・選択展開、内容のストリーム読み取り、作成、暗号化情報、パスワード、進捗、キャンセル、型付きエラーを実装。Release ビルドは警告・エラーともに 0 件。
- [x] 同じ NuGet を参照する検証アプリが .NET 11 と win-x64 NativeAOT で成功。7 形式の作成・一覧・読み取り・選択展開・全展開、日本語パス、暗号化 ZIP、キャンセル、危険な展開パスの拒否を確認。
- [x] 通知文と展開時のファイル処理の修正を反映した最終 NuGet を再作成。7 形式の空書庫を追加し、.NET 11 と NativeAOT で全項目に合格。
- [x] 最小対象の .NET 8 でも最終 NuGet の全項目に合格。検証アプリは `-p:SmokeTargetFramework=net8.0` で対象を切り替えられる。サンドボックス内の NuGet 監査情報取得警告はネットワークアクセス付きの再復元で解消。
- [x] xUnit の単体テストを追加。Windows x64 の .NET 8 で 38 件合格、失敗・スキップともに 0 件。`dotnet test --project tests/Arcdini.Tests -c Release` で実行できる。
- [x] ライブラリと両方のテストを `Arcdini.slnx` に統合。存在しないローカル NuGet バージョンを指定した状態で、ルートの `dotnet build -c Release` は警告・エラー 0 件、`dotnet test -c Release` は 38 件合格。
