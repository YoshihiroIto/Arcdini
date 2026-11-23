namespace Arcdini;

public enum ArchiveError
{
    UnsupportedFormat,
    UnsupportedFeature,
    InvalidArchive,
    PasswordRequired,
    IncorrectPassword,
    EntryNotFound,
    UnsafeEntry,
    DestinationExists,
    NativeFailure
}

public sealed class ArchiveException : IOException
{
    internal ArchiveException(ArchiveError error, string message, string? entryPath = null, int? nativeError = null)
        : base(message)
    {
        Error = error;
        EntryPath = entryPath;
        NativeError = nativeError;
    }

    public ArchiveError Error { get; }
    public string? EntryPath { get; }
    public int? NativeError { get; }
}
