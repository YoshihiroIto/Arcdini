using System.Buffers;

namespace Arcdini;

public sealed partial class ArchiveFile
{
    public void ExtractAll(
        string destinationDirectory,
        ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Extract(destinationDirectory, selection: null, selectPath: null, int.MaxValue,
            options ?? new(), progress, cancellationToken);

    /// <summary>Extracts entries for which the selector returns a destination-relative path.</summary>
    public int ExtractMatchingEntries(
        string destinationDirectory,
        Func<ArchiveEntry, string?> selectPath,
        ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int maximumEntries = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(selectPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        return Extract(destinationDirectory, selection: null, selectPath, maximumEntries,
            options ?? new(), progress, cancellationToken);
    }

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

        Extract(destinationDirectory, ids, selectPath: null, ids.Count, options ?? new(), progress, cancellationToken);
    }

    private int Extract(
        string destinationDirectory,
        HashSet<long>? selection,
        Func<ArchiveEntry, string?>? selectPath,
        int maximumEntries,
        ArchiveExtractionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        var root = ArchivePaths.GetFullPath(destinationDirectory);
        Acquire(cancellationToken);
        try
        {
            using var reader = NewReader(cancellationToken);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var directoryTimes = new Dictionary<string, ExtractionTimes>(pathComparer);
            List<(ArchiveEntry Entry, string Output, string Target)>? links = null;
            List<(string Path, bool IsDirectory)>? created =
                options.RemoveNewEntriesOnCancellation ? [] : null;
            HashSet<string>? linkPaths = null;
            long bytes = 0;
            var completed = 0;
            try
            {
                while (completed < maximumEntries && reader.MoveNext())
                {
                    var entry = reader.Current!;
                    if (selection is not null && !selection.Contains(entry.Id))
                    {
                        reader.Skip();
                        continue;
                    }

                    var relativePath = selectPath is null ? entry.Path : selectPath(entry);
                    if (relativePath is null)
                    {
                        reader.Skip();
                        continue;
                    }

                    var components = ArchivePaths.ValidateEntryPath(relativePath);
                    var output = components.Length == 0 ? root : Path.Join(root, Path.Join(components));
                    if (linkPaths is not null && HasLinkAncestor(output, linkPaths))
                    {
                        reader.Skip();
                    }
                    else if (entry.Kind == ArchiveEntryKind.Directory)
                    {
                        ArchivePaths.EnsureDirectory(output, entry.Path, created);
                        if (components.Length != 0)
                        {
                            var times = GetExtractionTimes(reader, entry, options);
                            if (times != default)
                                directoryTimes[output] = times;
                        }
                        reader.Skip();
                    }
                    else if (entry.Kind == ArchiveEntryKind.File && components.Length != 0)
                    {
                        WriteExtractedFile(reader, entry, output, options, buffer, progress,
                            completed, ref bytes, created, cancellationToken);
                    }
                    else if (options.RestoreSymbolicLinks && entry.Kind == ArchiveEntryKind.SymbolicLink)
                    {
                        if (components.Length != 0 && reader.LinkTarget is { } target)
                        {
                            (links ??= []).Add((entry, output, target));
                            (linkPaths ??= new HashSet<string>(pathComparer)).Add(output);
                        }
                        reader.Skip();
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

                if (links is not null)
                {
                    foreach (var link in links)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        RestoreSymbolicLink(root, link.Output, link.Target, link.Entry, options, created);
                    }
                }

                foreach (var pair in directoryTimes.OrderByDescending(pair => pair.Key.Length))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ArchivePaths.RejectReparsePoint(pair.Key, pair.Key);
                    ApplyTimestamps(new DirectoryInfo(pair.Key), pair.Value);
                }
                return completed;
            }
            catch (OperationCanceledException)
            {
                RemoveCreatedEntries(created);
                throw;
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

    private static void RemoveCreatedEntries(List<(string Path, bool IsDirectory)>? created)
    {
        if (created is null)
            return;

        for (var i = created.Count - 1; i >= 0; --i)
        {
            var (path, isDirectory) = created[i];
            try
            {
                if (isDirectory)
                    Directory.Delete(path);
                else
                    File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Preserve the cancellation even if another process is using an output path.
            }
        }
    }

    private static bool HasLinkAncestor(string path, HashSet<string> linkPaths)
    {
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            if (linkPaths.Contains(parent))
                return true;
        }
        return false;
    }

    private static void RestoreSymbolicLink(
        string root, string output, string target, ArchiveEntry entry, ArchiveExtractionOptions options,
        List<(string Path, bool IsDirectory)>? created)
    {
        if (Path.IsPathRooted(target) || target.Length == 0 || target[0] is '/' or '\\' || target.Contains(':'))
            return;

        var parent = Path.GetDirectoryName(output)!;
        var relativeTarget = target.Replace('\\', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Join(parent, relativeTarget));
        var relative = Path.GetRelativePath(root, resolved);
        if (relative is ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            return;

        // The target must not lead outside through a link already present on disk.
        for (var candidate = resolved; candidate.Length > root.Length; candidate = Path.GetDirectoryName(candidate)!)
        {
            try
            {
                ArchivePaths.RejectReparsePoint(candidate, entry.Path);
            }
            catch (ArchiveException error) when (error.Error == ArchiveError.UnsafeEntry)
            {
                return;
            }
        }

        ArchivePaths.EnsureDirectory(parent, entry.Path, created);
        if (Directory.Exists(output))
            return;
        var existed = ArchivePaths.CheckDestination(output, options.OverwriteExistingFiles, entry.Path);
        var isDirectory = target.EndsWith('/') || target.EndsWith('\\') || Directory.Exists(resolved);
        var temporaryPath = Path.Join(parent, $".arcdini-{Guid.NewGuid():N}.tmp");
        try
        {
            if (isDirectory)
                Directory.CreateSymbolicLink(temporaryPath, relativeTarget);
            else
                File.CreateSymbolicLink(temporaryPath, relativeTarget);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var moved = false;
        try
        {
            if (isDirectory)
                Directory.Move(temporaryPath, output);
            else
                ArchivePaths.MoveIntoPlace(temporaryPath, output, options.OverwriteExistingFiles, entry.Path);
            moved = true;
            if (!existed)
                created?.Add((output, isDirectory));
        }
        finally
        {
            if (!moved)
            {
                if (isDirectory)
                    Directory.Delete(temporaryPath);
                else
                    File.Delete(temporaryPath);
            }
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
        List<(string Path, bool IsDirectory)>? created,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(output)!;
        ArchivePaths.EnsureDirectory(parent, entry.Path, created);
        var existed = ArchivePaths.CheckDestination(output, options.OverwriteExistingFiles, entry.Path);
        var times = GetExtractionTimes(reader, entry, options);
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
            if (times != default)
                ApplyTimestamps(new FileInfo(temporaryPath), times);
            ArchivePaths.MoveIntoPlace(temporaryPath, output, options.OverwriteExistingFiles, entry.Path);
            committed = true;
            if (!existed)
                created?.Add((output, false));
        }
        finally
        {
            if (!committed)
                File.Delete(temporaryPath);
        }
    }

    private static ExtractionTimes GetExtractionTimes(
        NativeReader reader, ArchiveEntry entry, ArchiveExtractionOptions options)
    {
        var (created, accessed) = options.PreserveCreationAndAccessTimes
            ? reader.GetCreationAndAccessTimes()
            : default;
        return new ExtractionTimes(created, accessed, options.PreserveLastWriteTime ? entry.LastWriteTime : null);
    }

    private static void ApplyTimestamps(FileSystemInfo output, ExtractionTimes times)
    {
        if (times.Created is { } created)
            output.CreationTimeUtc = created.UtcDateTime;
        if (times.Accessed is { } accessed)
            output.LastAccessTimeUtc = accessed.UtcDateTime;
        if (times.Modified is { } modified)
            output.LastWriteTimeUtc = modified.UtcDateTime;
    }

    private readonly record struct ExtractionTimes(DateTimeOffset? Created, DateTimeOffset? Accessed, DateTimeOffset? Modified);

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
