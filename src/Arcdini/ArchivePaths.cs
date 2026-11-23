namespace Arcdini;

internal static class ArchivePaths
{
    private static readonly char[] Separators = ['/', '\\'];
    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    internal static string[] Split(string path)
        => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(component => component != ".").ToArray();

    internal static string GetFullPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsMacOS())
            return fullPath;

        // macOS exposes system directories such as TMPDIR through these root-level aliases.
        var end = fullPath.IndexOf('/', 1);
        var component = end < 0 ? fullPath : fullPath[..end];
        if (component is "/tmp" or "/var" or "/etc")
        {
            var target = new DirectoryInfo(component).LinkTarget;
            if (target == "private" + component || target == "/private" + component)
                return "/private" + fullPath;
        }

        return fullPath;
    }

    internal static string[] ValidateEntryPath(string path)
    {
        var components = Split(path);
        if (path.Length == 0 || path[0] is '/' or '\\')
            throw UnsafePath(path);
        foreach (var component in components)
        {
            if (component.EndsWith('.') || component.EndsWith(' ') ||
                component.IndexOfAny(InvalidCharacters) >= 0 || component.Any(char.IsControl) ||
                IsDeviceName(component))
                throw UnsafePath(path);
        }
        return components;
    }

    internal static void EnsureDirectory(
        string path, string entryPath, List<(string Path, bool IsDirectory)>? created = null)
    {
        var root = Path.GetPathRoot(path)!;
        foreach (var part in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            root = Path.Join(root, part);
            var attributes = RejectReparsePoint(root, entryPath);
            if (attributes is null)
            {
                Directory.CreateDirectory(root);
                created?.Add((root, true));
            }
            else if ((attributes.Value & FileAttributes.Directory) == 0)
                throw Exists(root, entryPath);
        }
    }

    internal static FileAttributes? RejectReparsePoint(string path, string entryPath)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArchiveException(ArchiveError.UnsafeEntry, $"The destination contains a reparse point: {path}", entryPath);
        return attributes;
    }

    internal static FileStream CreateTemporaryFile(string directory, bool deleteOnClose = false)
    {
        var options = FileOptions.SequentialScan;
        if (deleteOnClose)
            options |= FileOptions.DeleteOnClose;
        var path = Path.Join(directory, $".arcdini-{Guid.NewGuid():N}.tmp");
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, options);
    }

    internal static bool CheckDestination(string destination, bool overwrite, string entryPath)
    {
        var attributes = RejectReparsePoint(destination, entryPath);
        if (attributes is { } existing && (!overwrite || (existing & FileAttributes.Directory) != 0))
            throw Exists(destination, entryPath);
        return attributes.HasValue;
    }

    internal static void MoveIntoPlace(string temporaryPath, string destination, bool overwrite, string entryPath)
    {
        CheckDestination(destination, overwrite, entryPath);
        try
        {
            File.Move(temporaryPath, destination, overwrite);
        }
        catch (IOException error) when (error.HResult is unchecked((int)0x80070050) or unchecked((int)0x800700B7))
        {
            throw Exists(destination, entryPath);
        }
    }

    internal static ArchiveException UnsafePath(string entryPath)
        => new(ArchiveError.UnsafeEntry, $"The archive path cannot be used as a Windows relative path: {entryPath}", entryPath);

    private static ArchiveException Exists(string path, string entryPath)
        => new(ArchiveError.DestinationExists, $"The destination already exists: {path}", entryPath);

    private static bool IsDeviceName(string component)
    {
        var dot = component.IndexOf('.');
        var stem = (dot < 0 ? component : component[..dot]).TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                 stem.StartsWith("LPT", StringComparison.Ordinal)) && "0123456789¹²³".Contains(stem[3]));
    }
}
