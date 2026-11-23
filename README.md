# Arcdini

Arcdini is a .NET library for working with archives using libarchive. It supports .NET 8 and later. The NuGet package includes a native Windows x64 DLL. Applications integrating Arcdini from source can also build a macOS dylib for arm64 or x64.

## Supported formats

| Format | List, extract, and read | Create |
| --- | --- | --- |
| ZIP | Standard ZIP, ZipCrypto, WinZip AES | Standard ZIP, WinZip AES-256 |
| 7z | Unencrypted 7z | Unencrypted 7z |
| TAR | Uncompressed, gzip, bzip2, lzip, xz, Zstandard | Uncompressed, gzip, bzip2, lzip, xz, Zstandard |

ZIP compression support follows libarchive. Deflate64, Shrink, Reduce, and Implode are unsupported. The native DLL is built without the RAR and RAR5 decoders. Listed entry sizes are uncompressed sizes.

## Usage

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

- Use `ExtractAll` to extract the entire archive. When you pass a directory ID to `ExtractEntries`, it also extracts all entries under that directory. IDs distinguish entries with the same name.
- `Open` and `Create` accept file paths and `Stream` instances. Arcdini reads input streams from their current position to the end and copies non-seekable input to a temporary file. Set `leaveOpen: true` to retain ownership of the stream you pass in.
- `OpenEntry` returns a forward-only read stream. Dispose it before starting another operation on the same `ArchiveFile`. Use separate `ArchiveFile` instances and inputs to read in parallel.
- Set `ArchiveReadOptions.Password` to read an encrypted archive and `ArchiveCreationOptions.Password` to create an encrypted ZIP. Use `IProgress<ArchiveProgress>` to receive progress updates during creation and extraction. Use `ArchiveException.Error` to identify the kind of error.
- By default, extraction creates regular files and directories and rejects archive links and existing reparse points. You can enable overwriting of existing files. On failure or cancellation, Arcdini removes temporary files and keeps files that finished extracting. Keep other processes from modifying the destination directory structure during extraction.
- Enable `RestoreSymbolicLinks` to restore symbolic links whose relative targets resolve within the extraction directory. Use `ArchiveEntrySource.SymbolicLink` to include a link when creating a ZIP or TAR archive.
- When creating an archive at a file path, Arcdini moves the finished archive into place after writing completes. When writing directly to an output stream, a failure can leave partial output in the stream.

## Build and verification

Open `Arcdini.slnx` in your IDE. On Windows x64 with the .NET 10 SDK or later, run the following commands from the repository root to build the library and run the unit tests.

```powershell
dotnet build -c Release
dotnet test -c Release
```

`Arcdini.Smoke` uses the local NuGet package. Build the package first, then run the smoke test project directly. Building the native DLL and running the smoke tests requires Windows x64, PowerShell 7, CMake 4.2 or later, the Visual Studio 2026 C++ tools, and the .NET 11 SDK.

```powershell
./native/Build-Windows.ps1
dotnet pack src/Arcdini -c Release -o artifacts/packages
dotnet run --project tests/Arcdini.Smoke -c Release
dotnet publish tests/Arcdini.Smoke -c Release -r win-x64 -p:PublishAot=true -o artifacts/smoke-aot
./artifacts/smoke-aot/Arcdini.Smoke.exe
```

The build scripts download libarchive 3.8.11, zlib 1.3.2, bzip2 1.0.8, liblzma 5.8.3, and Zstandard 1.5.7 and verify their SHA-256 checksums against pinned values. The native build links the compression libraries statically and uses Windows CNG for ZIP encryption.

The NuGet package copies `Arcdini/LICENSE` and `Arcdini/THIRD-PARTY-NOTICES.txt` to the build and publish outputs of applications that reference it. Include both files when distributing the application in a ZIP or installer.

To build the dylib on macOS, install PowerShell 7, CMake, and the Xcode Command Line Tools. Run `./native/Build-macOS.ps1 -Architecture arm64` or `./native/Build-macOS.ps1 -Architecture x86_64`. Bundle `artifacts/native/osx-*/libarcdini-libarchive.dylib` with your application along with the notices from the same directory and the Arcdini license.
