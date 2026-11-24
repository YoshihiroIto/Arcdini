namespace Arcdini;

public enum ArchiveEntryKind
{
    File,
    Directory,
    SymbolicLink,
    HardLink,
    Other
}

/// <summary>Metadata copied from an archive. IDs identify entries in archive order, starting at zero.</summary>
public sealed record ArchiveEntry(
    long Id,
    string Path,
    ArchiveEntryKind Kind,
    long? Size,
    DateTimeOffset? LastWriteTime,
    bool IsEncrypted);

/// <summary>Progress within one operation. BytesTransferred counts uncompressed bytes.</summary>
public readonly record struct ArchiveProgress(
    string EntryPath,
    long EntriesCompleted,
    long BytesTransferred);