using System.Buffers;

namespace Arcdini;

public sealed partial class ArchiveFile
{
    public void ExtractAll(
        string destinationDirectory,
        ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Extract(destinationDirectory, selection: null, options ?? new(), progress, cancellationToken);

    /// <summary>Extracts the given entries. A selected directory includes all of its descendants.</summary>
    public void ExtractEntries(
        IEnumerable<long> entryIds,
        string destinationDirectory,
        ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var ids = entryIds.ToHashSet();
        if (ids.Count == 0)
        {
            Acquire(cancellationToken);
            Release();
            return;
        }

        var entries = GetEntries(cancellationToken);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (id < 0 || id >= entries.Count)
                throw EntryNotFound(id);
            var entry = entries[(int)id];
            if (entry.Kind == ArchiveEntryKind.Directory)
                directories.Add(string.Join('/', ArchivePaths.ValidateEntryPath(entry.Path)));
        }
        if (directories.Count != 0)
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = string.Join('/', ArchivePaths.Split(entry.Path));
                if (IsInDirectory(path, directories))
                    ids.Add(entry.Id);
            }
        }

        Extract(destinationDirectory, ids, options ?? new(), progress, cancellationToken);
    }

    private void Extract(
        string destinationDirectory,
        HashSet<long>? selection,
        ArchiveExtractionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);
        Acquire(cancellationToken);
        try
        {
            using var reader = NewReader(cancellationToken);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            var directoryTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            long completed = 0;
            try
            {
                while ((selection is null || completed < selection.Count) && reader.MoveNext())
                {
                    var entry = reader.Current!;
                    if (selection is not null && !selection.Contains(entry.Id))
                    {
                        reader.Skip();
                        continue;
                    }

                    var components = ArchivePaths.ValidateEntryPath(entry.Path);
                    var output = components.Length == 0 ? root : Path.Join(root, Path.Join(components));
                    if (entry.Kind == ArchiveEntryKind.Directory)
                    {
                        ArchivePaths.EnsureDirectory(output, entry.Path);
                        if (components.Length != 0 && options.PreserveLastWriteTime && entry.LastWriteTime is { } time)
                            directoryTimes[output] = time;
                        reader.Skip();
                    }
                    else if (entry.Kind == ArchiveEntryKind.File && components.Length != 0)
                    {
                        WriteExtractedFile(reader, entry, output, options, buffer, progress, completed, ref bytes, cancellationToken);
                    }
                    else
                    {
                        throw new ArchiveException(ArchiveError.UnsafeEntry, "Only regular files and directories can be extracted.", entry.Path);
                    }
                    completed++;
                    progress?.Report(new(entry.Path, completed, bytes));
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (selection is not null && completed != selection.Count)
                    throw new ArchiveException(ArchiveError.EntryNotFound, "The archive ended before all selected entries were found.");

                foreach (var pair in directoryTimes.OrderByDescending(pair => pair.Key.Length))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ArchivePaths.RejectReparsePoint(pair.Key, pair.Key);
                    Directory.SetLastWriteTimeUtc(pair.Key, pair.Value.UtcDateTime);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
        finally
        {
            Release();
        }
    }

    private static void WriteExtractedFile(
        NativeReader reader,
        ArchiveEntry entry,
        string output,
        ArchiveExtractionOptions options,
        byte[] buffer,
        IProgress<ArchiveProgress>? progress,
        long completed,
        ref long bytes,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(output)!;
        ArchivePaths.EnsureDirectory(parent, entry.Path);
        ArchivePaths.CheckDestination(output, options.OverwriteExistingFiles, entry.Path);
        var temporary = ArchivePaths.CreateTemporaryFile(parent);
        var temporaryPath = temporary.Name;
        var committed = false;
        try
        {
            using (temporary)
            {
                long entryBytes = 0;
                int count;
                while ((count = reader.Read(buffer)) != 0)
                {
                    temporary.Write(buffer, 0, count);
                    entryBytes += count;
                    bytes += count;
                    progress?.Report(new(entry.Path, completed, bytes));
                }
                if (entry.Size is long size && entryBytes != size)
                    throw new ArchiveException(ArchiveError.InvalidArchive, "The extracted size does not match the entry header.", entry.Path);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (options.PreserveLastWriteTime && entry.LastWriteTime is { } time)
                File.SetLastWriteTimeUtc(temporaryPath, time.UtcDateTime);
            ArchivePaths.MoveIntoPlace(temporaryPath, output, options.OverwriteExistingFiles, entry.Path);
            committed = true;
        }
        finally
        {
            if (!committed)
                File.Delete(temporaryPath);
        }
    }

    private static bool IsInDirectory(string path, HashSet<string> directories)
    {
        if (directories.Contains("") || directories.Contains(path))
            return true;
        var end = path.LastIndexOf('/');
        while (end >= 0)
        {
            if (directories.Contains(path[..end]))
                return true;
            end = path.LastIndexOf('/', end - 1);
        }
        return false;
    }
}
