# Arcdini

libarchive を使って圧縮アーカイブを扱う .NET ライブラリです。Windows x64 が対象で、.NET 8 以降のアプリから利用できます。NuGet パッケージに専用のネイティブ DLL を同梱します。

## 対応形式

| 形式 | 一覧・展開・読み取り | 作成 |
| --- | --- | --- |
| ZIP | 通常の ZIP、ZipCrypto、WinZip AES | 通常の ZIP、WinZip AES-256 |
| 7z | 暗号化されていない 7z | 暗号化されていない 7z |
| TAR | 非圧縮、gzip、bzip2、xz、Zstandard | 非圧縮、gzip、bzip2、xz、Zstandard |

扱える ZIP 圧縮方式は libarchive に準拠します。Deflate64、Shrink、Reduce、Implode は対象外です。RAR・RAR5 のデコーダーはネイティブ DLL のコンパイル対象から除外しています。一覧のサイズは展開後の値です。

## 使い方

```csharp
using Arcdini;

ArchiveFile.Create(
    "example.zip",
    [
        ArchiveEntrySource.FromFile("report.txt", "資料/report.txt"),
        ArchiveEntrySource.Directory("空フォルダー")
    ],
    new(ArchiveFormat.Zip),
    cancellationToken: cancellationToken);

using var archive = ArchiveFile.Open("example.zip");
var entries = archive.GetEntries(cancellationToken);
var report = entries.Single(entry => entry.Path == "資料/report.txt");

using (var content = archive.OpenEntry(report.Id, cancellationToken))
{
    content.CopyTo(destinationStream);
}

archive.ExtractEntries([report.Id], "output", cancellationToken: cancellationToken);
```

- 全体の展開には `ExtractAll` を使います。`ExtractEntries` でフォルダーの ID を指定すると、その配下も展開されます。同名エントリも ID で区別できます。
- `Open` と `Create` はファイルパスと `Stream` を受け付けます。ストリーム入力は現在位置から末尾までを使います。シークできない入力は一時ファイルにコピーします。`leaveOpen: true` を指定すると、渡したストリームの所有権を呼び出し元に保持できます。
- `OpenEntry` が返すストリームは前方読み取り専用です。同じ `ArchiveFile` で次の操作を始める前に、そのストリームを破棄してください。並行して読む場合は、別々の `ArchiveFile` と入力を使えます。
- 読み取りパスワードは `ArchiveReadOptions.Password`、ZIP の作成パスワードは `ArchiveCreationOptions.Password` に渡します。作成・展開の進捗は `IProgress<ArchiveProgress>` で受け取れます。書庫処理のエラーは `ArchiveException.Error` で判別できます。
- 展開先では通常ファイルとディレクトリを作成し、書庫内のリンクや既存のリパースポイントを拒否します。既存ファイルの置き換えはオプションで有効にできます。処理中の一時ファイルは失敗・キャンセル時に削除し、すでに展開を終えたファイルは残ります。展開中は別のプロセスから展開先のディレクトリ構成を変更しないでください。
- ファイルへの書庫作成は完了後に書庫を配置します。出力ストリームに直接作成した場合は、失敗時に書き込み済みのデータが残ることがあります。

## ビルドと検証

IDE では `Arcdini.slnx` を開きます。ライブラリのビルドと単体テストは、Windows x64 と .NET 10 以降の SDK で、リポジトリのルートから実行できます。

```powershell
dotnet build -c Release
dotnet test -c Release
```

`Arcdini.Smoke` はローカル NuGet を参照するので、パック後にプロジェクトを指定して実行します。ネイティブ DLL のビルドとスモークテストには、Windows x64、PowerShell 7、CMake 4.2 以降、Visual Studio 2026 の C++ ツール、.NET 11 SDK が必要です。

```powershell
./native/Build-Windows.ps1
dotnet pack src/Arcdini -c Release -o artifacts/packages
dotnet run --project tests/Arcdini.Smoke -c Release
dotnet publish tests/Arcdini.Smoke -c Release -r win-x64 -p:PublishAot=true -o artifacts/smoke-aot
./artifacts/smoke-aot/Arcdini.Smoke.exe
```

ビルドスクリプトは SHA-256 を固定した libarchive 3.8.11、zlib 1.3.2、bzip2 1.0.8、liblzma 5.8.3、Zstandard 1.5.7 のソースを取得します。圧縮ライブラリは静的にリンクし、ZIP の暗号処理には Windows CNG を使います。

NuGet を参照したアプリのビルド・発行先には `Arcdini/LICENSE` と `Arcdini/THIRD-PARTY-NOTICES.txt` がコピーされます。ZIP やインストーラーを組み立てる際にも、そのファイルを配布物に含めてください。
