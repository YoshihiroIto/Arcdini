using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Arcdini;

const string fileName = "資料/日本語🌐.txt";
var payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Arcdini 日本語\n", 32_000)));
var timestamp = new DateTimeOffset(2025, 1, 2, 3, 4, 6, TimeSpan.Zero);
var root = Directory.CreateTempSubdirectory("arcdini-smoke-").FullName;
try
{
    Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "arcdini-libarchive.dll")), "native DLL in output");
    Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Arcdini", "LICENSE")), "package license in output");
    var notices = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Arcdini", "THIRD-PARTY-NOTICES.txt"));
    Check(notices.Contains("Tim Kientzle") && notices.Contains("Zstandard"), "third-party notices in output");

    foreach (var format in Enum.GetValues<ArchiveFormat>())
    {
        RoundTrip(format);
        EmptyArchive(format);
    }
    StreamOwnershipAndOffset();
    DuplicateEntries();
    EncryptedZip();
    CancellationAndOverwrite();
    UnsafeEntries();
    UnsupportedArchive();
    Console.WriteLine("Arcdini smoke tests passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

void RoundTrip(ArchiveFormat format)
{
    var original = Path.Combine(root, "input.txt");
    File.WriteAllBytes(original, payload);
    File.SetLastWriteTimeUtc(original, timestamp.UtcDateTime);
    var archivePath = Path.Combine(root, format + ".archive");
    var progress = new InlineProgress();
    ArchiveFile.Create(archivePath,
        [
            ArchiveEntrySource.FromFile(original, fileName),
            ArchiveEntrySource.Directory("資料", timestamp),
            ArchiveEntrySource.Directory("空", timestamp),
            ArchiveEntrySource.FromStream("zero.txt", 0, () => new MemoryStream())
        ],
        new(format), progress: progress);
    Check(progress.Last.EntriesCompleted == 4 && progress.Last.BytesTransferred == payload.Length, $"{format} creation progress");

    using var archive = ArchiveFile.Open(archivePath);
    var entries = archive.GetEntries();
    var file = entries.Single(entry => entry.Path == fileName);
    Check(entries.Count == 4 && file.Kind == ArchiveEntryKind.File && file.Size == payload.Length, $"{format} metadata");
    Check(file.LastWriteTime == timestamp && !file.IsEncrypted, $"{format} timestamp and encryption flag");
    using (var content = archive.OpenEntry(file.Id))
    {
        Check(!content.CanSeek && !content.CanWrite, $"{format} stream capabilities");
        Check(ReadAll(content).SequenceEqual(payload), $"{format} entry stream");
    }

    var folder = entries.Single(entry => entry.Kind == ArchiveEntryKind.Directory && entry.Path.TrimEnd('/') == "資料");
    var selected = Path.Combine(root, format + "-selected");
    archive.ExtractEntries([folder.Id], selected);
    Check(File.ReadAllBytes(Path.Combine(selected, fileName)).SequenceEqual(payload), $"{format} selected folder");
    Check(!Directory.Exists(Path.Combine(selected, "空")), $"{format} skips unselected folder");
    Check(File.GetLastWriteTimeUtc(Path.Combine(selected, fileName)) == timestamp.UtcDateTime, $"{format} extracted timestamp");
    Check(Directory.GetLastWriteTimeUtc(Path.Combine(selected, "資料")) == timestamp.UtcDateTime, $"{format} directory timestamp");

    var entire = Path.Combine(root, format + "-all");
    progress = new InlineProgress();
    archive.ExtractAll(entire, progress: progress);
    Check(Directory.Exists(Path.Combine(entire, "空")) &&
        new FileInfo(Path.Combine(entire, "zero.txt")).Length == 0, $"{format} full extraction");
    Check(progress.Last.EntriesCompleted == 4 && progress.Last.BytesTransferred == payload.Length, $"{format} extraction progress");
    Console.WriteLine($"{format}: create, list, stream, select and extract");
}

void StreamOwnershipAndOffset()
{
    using var prefixed = new MemoryStream();
    prefixed.Write(new byte[37]);
    using (var output = new NonSeekableStream(prefixed))
    {
        ArchiveFile.Create(output, [ArchiveEntrySource.FromStream(fileName, payload.Length, () => new MemoryStream(payload))],
            new(ArchiveFormat.SevenZip));
        Check(output.IsDisposed, "writer owns stream by default");
    }

    prefixed.Position = 37;
    using (var archive = ArchiveFile.Open(prefixed, leaveOpen: true))
    {
        using var content = archive.OpenEntry(archive.GetEntries().Single().Id);
        archive.Dispose();
        Check(ReadAll(content).SequenceEqual(payload), "read from nonzero stream origin after owner disposal");
    }
    Check(prefixed.CanRead, "leaveOpen keeps original source open");

    prefixed.Position = 37;
    using var input = new NonSeekableStream(prefixed);
    using var spooled = ArchiveFile.Open(input, leaveOpen: true);
    Check(!input.IsDisposed, "leaveOpen keeps non-seekable source open");
    using var spooledContent = spooled.OpenEntry(spooled.GetEntries().Single().Id);
    Check(ReadAll(spooledContent).SequenceEqual(payload), "non-seekable 7z input");
}

static void EmptyArchive(ArchiveFormat format)
{
    using var stream = new MemoryStream();
    ArchiveFile.Create(stream, [], new(format), leaveOpen: true);
    stream.Position = 0;
    using var archive = ArchiveFile.Open(stream, leaveOpen: true);
    Check(archive.GetEntries().Count == 0, $"{format} empty archive");
}

void DuplicateEntries()
{
    using var stream = ExternalZip(("same.txt", "first"), ("same.txt", "second"));
    using var archive = ArchiveFile.Open(stream, leaveOpen: true);
    var entries = archive.GetEntries();
    Check(entries.Count == 2 && entries[0].Id != entries[1].Id, "duplicate entries have distinct IDs");
    var destination = Path.Combine(root, "duplicates");
    archive.ExtractEntries([entries[1].Id], destination);
    Check(File.ReadAllText(Path.Combine(destination, "same.txt")) == "second", "select duplicate by ID");
}

void EncryptedZip()
{
    using var stream = new MemoryStream();
    const string password = "テスト-AES";
    ArchiveFile.Create(stream,
        [ArchiveEntrySource.FromStream(fileName, payload.Length, () => new MemoryStream(payload))],
        new(ArchiveFormat.Zip) { Password = password }, leaveOpen: true);

    foreach (var candidate in new[] { null, "incorrect", password })
    {
        stream.Position = 0;
        using var archive = ArchiveFile.Open(stream, new() { Password = candidate }, leaveOpen: true);
        var entry = archive.GetEntries().Single();
        Check(entry.IsEncrypted, "ZIP encryption flag");
        using var content = archive.OpenEntry(entry.Id);
        if (candidate is null)
            ExpectError(ArchiveError.PasswordRequired, () => content.CopyTo(Stream.Null));
        else if (candidate != password)
            ExpectError(ArchiveError.IncorrectPassword, () => content.CopyTo(Stream.Null));
        else
            Check(ReadAll(content).SequenceEqual(payload), "encrypted ZIP round trip");
    }
}

void CancellationAndOverwrite()
{
    var path = Path.Combine(root, "cancel.zip");
    var entry = ArchiveEntrySource.FromStream("file.txt", payload.Length, () => new MemoryStream(payload));
    var options = new ArchiveCreationOptions(ArchiveFormat.Zip);
    ArchiveFile.Create(path, [entry], options);
    var previousArchive = File.ReadAllBytes(path);

    using var creation = new CancellationTokenSource();
    ExpectCancellation(() => ArchiveFile.Create(path, [entry], options, overwrite: true,
        progress: new InlineProgress(_ => creation.Cancel()), cancellationToken: creation.Token));
    Check(File.ReadAllBytes(path).SequenceEqual(previousArchive), "cancelled archive creation preserves previous archive");

    using var archive = ArchiveFile.Open(path);
    var dest = Directory.CreateDirectory(Path.Combine(root, "cancel-extract")).FullName;
    var target = Path.Combine(dest, "file.txt");
    File.WriteAllText(target, "previous");
    ExpectError(ArchiveError.DestinationExists, () => archive.ExtractAll(dest));
    var overwrite = new ArchiveExtractionOptions { OverwriteExistingFiles = true };
    using var extraction = new CancellationTokenSource();
    ExpectCancellation(() => archive.ExtractAll(dest, overwrite,
        new InlineProgress(_ => extraction.Cancel()), extraction.Token));
    Check(File.ReadAllText(target) == "previous", "cancelled extraction preserves existing file");
    Check(Directory.GetFiles(dest).SequenceEqual([target]), "cancelled extraction removes temporary file");
    archive.ExtractAll(dest, overwrite);
    Check(File.ReadAllBytes(target).SequenceEqual(payload), "successful overwrite");
}

void UnsafeEntries()
{
    var dest = Path.Combine(root, "unsafe-output");
    foreach (var name in new[] { "../outside.txt", "..\\outside.txt", "/absolute", "C:/drive", "file.txt:stream", "folder/NUL.txt" })
    {
        using var zip = ExternalZip((name, "no"));
        using var archive = ArchiveFile.Open(zip);
        ExpectError(ArchiveError.UnsafeEntry, () => archive.ExtractAll(dest));
    }
    using var tar = new MemoryStream();
    using (var writer = new TarWriter(tar, leaveOpen: true))
    {
        writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../other" });
        writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "hard") { LinkName = "link" });
    }
    tar.Position = 0;
    using var tarArchive = ArchiveFile.Open(tar);
    var entries = tarArchive.GetEntries();
    Check(entries.Select(entry => entry.Kind).SequenceEqual([ArchiveEntryKind.SymbolicLink, ArchiveEntryKind.HardLink]), "link metadata");
    ExpectError(ArchiveError.UnsafeEntry, () => tarArchive.ExtractAll(dest));
    ExpectError(ArchiveError.UnsafeEntry, () => tarArchive.ExtractEntries([entries[1].Id], dest));
    Check(!Directory.Exists(dest) && !File.Exists(Path.Combine(root, "outside.txt")), "unsafe paths leave no output");
}

static void UnsupportedArchive()
{
    foreach (var signature in new byte[][] { [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00], [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00] })
    {
        using var archive = ArchiveFile.Open(new MemoryStream(signature));
        ExpectError(ArchiveError.UnsupportedFormat, () => archive.GetEntries());
    }
}

static MemoryStream ExternalZip(params (string Name, string Content)[] entries)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open());
            writer.Write(entry.Content);
        }
    }
    stream.Position = 0;
    return stream;
}

static byte[] ReadAll(Stream input)
{
    using var output = new MemoryStream();
    input.CopyTo(output);
    return output.ToArray();
}

static void ExpectError(ArchiveError error, Action action)
{
    try { action(); }
    catch (ArchiveException exception) when (exception.Error == error) { return; }
    throw new InvalidOperationException($"Expected {error}.");
}

static void ExpectCancellation(Action action)
{
    try { action(); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Expected cancellation.");
}

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException($"Failed: {description}");
}

internal sealed class InlineProgress(Action<ArchiveProgress>? onReport = null) : IProgress<ArchiveProgress>
{
    public ArchiveProgress Last { get; private set; }

    public void Report(ArchiveProgress value)
    {
        if (value.BytesTransferred < Last.BytesTransferred || value.EntriesCompleted < Last.EntriesCompleted)
            throw new InvalidOperationException("Progress went backwards.");
        Last = value;
        onReport?.Invoke(value);
    }
}

internal sealed class NonSeekableStream(Stream inner) : Stream
{
    public bool IsDisposed { get; private set; }
    public override bool CanRead => !IsDisposed && inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => !IsDisposed && inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
