namespace Arcdini;

public enum ArchiveFormat
{
    Zip,
    SevenZip,
    Tar,
    TarGzip,
    TarBzip2,
    TarXz,
    TarZstandard,
    TarLzip
}

public sealed class ArchiveReadOptions
{
    public string? Password { get; init; }
}

public sealed class ArchiveExtractionOptions
{
    public bool OverwriteExistingFiles { get; init; }
    public bool PreserveLastWriteTime { get; init; } = true;
    public bool PreserveCreationAndAccessTimes { get; init; }
    public bool RestoreSymbolicLinks { get; init; }
    public bool RemoveNewEntriesOnCancellation { get; init; }
}

public sealed class ArchiveCreationOptions(ArchiveFormat format)
{
    public ArchiveFormat Format { get; } = format;

    /// <summary>When supplied for ZIP, encrypt files with WinZip AES-256.</summary>
    public string? Password { get; init; }
}
