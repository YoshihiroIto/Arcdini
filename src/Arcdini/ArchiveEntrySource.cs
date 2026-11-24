namespace Arcdini;

/// <summary>Describes one file or directory to write to an archive.</summary>
public sealed class ArchiveEntrySource
{
    private readonly string? _filePath;
    private readonly Func<Stream>? _openStream;

    private ArchiveEntrySource(string path, long size, DateTimeOffset? lastWriteTime,
        string? filePath = null, Func<Stream>? openStream = null, string? linkTarget = null)
    {
        var components = ArchivePaths.ValidateEntryPath(path);
        if (components.Length is 0)
            throw ArchivePaths.UnsafePath(path);
        Path = string.Join('/', components);
        Size = size;
        LastWriteTime = lastWriteTime;
        _filePath = filePath;
        _openStream = openStream;
        LinkTarget = linkTarget;
    }

    public string Path { get; }
    internal long Size { get; }
    internal DateTimeOffset? LastWriteTime { get; }
    internal string? LinkTarget { get; }
    internal bool IsDirectory => _filePath is null && _openStream is null && LinkTarget is null;

    public static ArchiveEntrySource FromFile(string filePath, string? entryPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var fullPath = System.IO.Path.GetFullPath(filePath);
        return new ArchiveEntrySource(entryPath ?? System.IO.Path.GetFileName(fullPath), 0, null, filePath: fullPath);
    }

    /// <summary>Creates a file entry. Arcdini calls the factory once and disposes its returned stream.</summary>
    public static ArchiveEntrySource FromStream(
        string entryPath, long size, Func<Stream> openStream, DateTimeOffset? lastWriteTime = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryPath);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentNullException.ThrowIfNull(openStream);
        return new ArchiveEntrySource(entryPath, size, lastWriteTime, openStream: openStream);
    }

    public static ArchiveEntrySource Directory(string entryPath, DateTimeOffset? lastWriteTime = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryPath);
        return new ArchiveEntrySource(entryPath, 0, lastWriteTime);
    }

    public static ArchiveEntrySource SymbolicLink(
        string entryPath, string linkTarget, DateTimeOffset? lastWriteTime = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryPath);
        ArgumentException.ThrowIfNullOrEmpty(linkTarget);
        if (linkTarget.Contains('\0'))
            throw new ArgumentException("A symbolic link target cannot contain NUL.", nameof(linkTarget));
        return new ArchiveEntrySource(entryPath, 0, lastWriteTime, linkTarget: linkTarget);
    }

    internal Stream? OpenContent()
    {
        if (_filePath is { })
            return File.OpenRead(_filePath);
        if (_openStream is null)
            return null;
        return _openStream() ?? throw new InvalidOperationException("The entry stream factory returned null.");
    }

    internal long GetSize(Stream? stream) => _filePath is { } ? stream!.Length : Size;

    internal DateTimeOffset? GetLastWriteTime() => _filePath is { }
        ? new DateTimeOffset(File.GetLastWriteTimeUtc(_filePath))
        : LastWriteTime;
}