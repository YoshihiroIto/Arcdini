using System.Buffers;

namespace Arcdini;

public sealed partial class ArchiveFile
{
    /// <summary>Creates an archive at the given path. The finished archive is moved into place.</summary>
    public static void Create(
        string path,
        IEnumerable<ArchiveEntrySource> entries,
        ArchiveCreationOptions options,
        bool overwrite = false,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ValidateCreation(entries, options, cancellationToken);
        var output = ArchivePaths.GetFullPath(path);
        ArchivePaths.CheckDestination(output, overwrite, path);
        var temporary = ArchivePaths.CreateTemporaryFile(Path.GetDirectoryName(output)!);
        var temporaryPath = temporary.Name;
        var committed = false;
        try
        {
            using (temporary)
                CreateArchive(temporary, entries, options, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ArchivePaths.MoveIntoPlace(temporaryPath, output, overwrite, path);
            committed = true;
        }
        finally
        {
            if (!committed)
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Writes an archive to the current position of a writable stream.</summary>
    /// <remarks>On failure, the stream can contain partial output.</remarks>
    public static void Create(
        Stream destination,
        IEnumerable<ArchiveEntrySource> entries,
        ArchiveCreationOptions options,
        bool leaveOpen = false,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The archive destination must be writable.", nameof(destination));
        ValidateCreation(entries, options, cancellationToken);
        try
        {
            CreateArchive(destination, entries, options, progress, cancellationToken);
        }
        finally
        {
            if (!leaveOpen)
                destination.Dispose();
        }
    }

    private static void CreateArchive(
        Stream destination,
        IEnumerable<ArchiveEntrySource> entries,
        ArchiveCreationOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var writer = new NativeWriter(destination, options, cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            long bytes = 0;
            long completed = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(entry);
                using var source = entry.OpenContent();
                var size = entry.GetSize(source);
                writer.WriteHeader(entry, size, entry.GetLastWriteTime());
                if (source is not null)
                {
                    long remaining = size;
                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var count = source.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, remaining)));
                        if (count == 0)
                            throw new EndOfStreamException($"The source for {entry.Path} ended before its declared size.");
                        writer.Write(buffer.AsSpan(0, count), entry.Path);
                        remaining -= count;
                        bytes += count;
                        progress?.Report(new(entry.Path, completed, bytes));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (source.ReadByte() != -1)
                        throw new IOException($"The source for {entry.Path} exceeds its declared size.");
                }
                writer.FinishEntry(entry.Path);
                completed++;
                progress?.Report(new(entry.Path, completed, bytes));
            }
            cancellationToken.ThrowIfCancellationRequested();
            writer.Complete();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void ValidateCreation(
        IEnumerable<ArchiveEntrySource> entries, ArchiveCreationOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Format))
            throw new ArgumentOutOfRangeException(nameof(options));
        NativeStatus.ValidatePassword(options.Password);
        if (options.Password is not null && options.Format != ArchiveFormat.Zip)
            throw new ArchiveException(ArchiveError.UnsupportedFeature, "Arcdini only supports encryption when creating ZIP archives.");
        NativeStatus.CheckPlatform();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
