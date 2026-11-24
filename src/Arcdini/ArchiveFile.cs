namespace Arcdini;

/// <summary>Opens a ZIP, 7z or TAR archive for sequential reading.</summary>
public sealed partial class ArchiveFile : IDisposable
{
    private readonly Stream _source;
    private readonly long _origin;
    private readonly bool _ownsSource;
    private readonly string? _password;
    private readonly Lock _gate = new();
    private bool _disposed;
    private bool _active;
    private IReadOnlyList<ArchiveEntry>? _entries;

    private ArchiveFile(Stream source, bool ownsSource, string? password)
    {
        _source = source;
        _origin = source.Position;
        _ownsSource = ownsSource;
        _password = password;
    }

    public static ArchiveFile Open(string path, ArchiveReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        NativeStatus.CheckPlatform();
        NativeStatus.ValidatePassword(options?.Password);
        return new ArchiveFile(File.OpenRead(path), ownsSource: true, options?.Password);
    }

    /// <summary>Reads from the current position through the end of the stream.</summary>
    /// <remarks>Non-seekable inputs are copied to a temporary file. Only one operation can use this archive at a time.</remarks>
    public static ArchiveFile Open(
        Stream source,
        ArchiveReadOptions? options = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The archive source must be readable.", nameof(source));
        NativeStatus.CheckPlatform();
        NativeStatus.ValidatePassword(options?.Password);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.CanSeek)
            return new ArchiveFile(source, ownsSource: !leaveOpen, options?.Password);

        FileStream? temporary = null;
        try
        {
            try
            {
                temporary = ArchivePaths.CreateTemporaryFile(Path.GetTempPath(), deleteOnClose: true);
                Copy(source, temporary, cancellationToken);
            }
            finally
            {
                if (!leaveOpen)
                    source.Dispose();
            }

            temporary.Position = 0;
            return new ArchiveFile(temporary, ownsSource: true, options?.Password);
        }
        catch
        {
            temporary?.Dispose();
            throw;
        }
    }

    public IReadOnlyList<ArchiveEntry> GetEntries(CancellationToken cancellationToken = default)
    {
        Acquire(cancellationToken);
        try
        {
            if (_entries is { })
                return _entries;

            using var reader = NewReader(cancellationToken);
            var entries = new List<ArchiveEntry>();
            while (reader.MoveNext())
            {
                entries.Add(reader.Current!);
                reader.Skip();
            }

            return _entries = entries.AsReadOnly();
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Enumerates entry metadata in archive order and releases the reader when enumeration ends.</summary>
    public IEnumerable<ArchiveEntry> EnumerateEntries(CancellationToken cancellationToken = default)
    {
        Acquire(cancellationToken);
        try
        {
            using var reader = NewReader(cancellationToken);
            while (reader.MoveNext())
            {
                yield return reader.Current!;
                reader.Skip();
            }
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Opens a forward-only stream. Dispose it before another operation on this archive.</summary>
    public Stream OpenEntry(long entryId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryId);
        return OpenEntry(entry => entry.Id == entryId, cancellationToken) ?? throw EntryNotFound(entryId);
    }

    /// <summary>Opens the first matching entry, or returns null when none matches.</summary>
    public Stream? OpenEntry(Func<ArchiveEntry, bool> matches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matches);
        Acquire(cancellationToken);
        NativeReader? reader = null;
        try
        {
            reader = NewReader(cancellationToken);
            while (reader.MoveNext())
            {
                var entry = reader.Current!;
                if (matches(entry))
                {
                    if (entry.Kind != ArchiveEntryKind.File)
                        throw new ArchiveException(ArchiveError.UnsupportedFeature,
                            "Only regular files have readable data.", entry.Path);
                    return new EntryReadStream(reader, Release);
                }

                reader.Skip();
            }

            reader.Dispose();
            Release();
            return null;
        }
        catch
        {
            try
            {
                reader?.Dispose();
            }
            finally
            {
                Release();
            }

            throw;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_active && _ownsSource)
                _source.Dispose();
        }
    }

    private NativeReader NewReader(CancellationToken cancellationToken)
    {
        _source.Position = _origin;
        return new NativeReader(_source, _origin, _password, cancellationToken);
    }

    private void Acquire(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active)
                throw new InvalidOperationException("Another operation or entry stream is using this archive.");
            _active = true;
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            _active = false;
            if (_disposed && _ownsSource)
                _source.Dispose();
        }
    }

    private static ArchiveException EntryNotFound(long id)
        => new(ArchiveError.EntryNotFound, $"Entry ID {id} does not exist.");

    private static void Copy(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = source.Read(buffer);
                if (count is 0)
                    break;
                destination.Write(buffer, 0, count);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}