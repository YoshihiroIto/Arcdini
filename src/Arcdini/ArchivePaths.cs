using System.Buffers;

namespace Arcdini;

internal static class ArchivePaths
{
    private static readonly SearchValues<char> Separators = SearchValues.Create("/\\");
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create(
        "<>:\"|?*" +
        // Unicode control characters in the C0 and C1 ranges, including DEL.
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F" +
        "\u007F\u0080\u0081\u0082\u0083\u0084\u0085\u0086\u0087\u0088\u0089\u008A\u008B\u008C\u008D\u008E\u008F" +
        "\u0090\u0091\u0092\u0093\u0094\u0095\u0096\u0097\u0098\u0099\u009A\u009B\u009C\u009D\u009E\u009F");

    internal static string[] Split(string path)
        => Split(path, validate: false);

    private static string[] Split(string path, bool validate)
    {
        var span = path.AsSpan();
        var components = new List<string>();
        foreach (var range in span.SplitAny(Separators))
        {
            var component = span[range];
            if (component.IsEmpty || component is ".")
                continue;

            if (validate && (component[^1] is '.' or ' ' ||
                             component.ContainsAny(InvalidCharacters) || IsDeviceName(component)))
                throw UnsafePath(path);

            components.Add(component.ToString());
        }

        return components.ToArray();
    }

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
        if (path.Length is 0 || path[0] is '/' or '\\')
            throw UnsafePath(path);

        return Split(path, validate: true);
    }

    internal static void EnsureDirectory(
        string path, string entryPath, List<(string Path, bool IsDirectory)>? created = null)
    {
        var root = Path.GetPathRoot(path)!;
        var relativePath = path.AsSpan(root.Length);
        foreach (var range in relativePath.Split(Path.DirectorySeparatorChar))
        {
            var part = relativePath[range];
            if (part.IsEmpty)
                continue;

            root = Path.Join(root.AsSpan(), part);
            var attributes = RejectReparsePoint(root, entryPath);
            if (attributes is null)
            {
                Directory.CreateDirectory(root);
                created?.Add((root, true));
            }
            else if ((attributes.Value & FileAttributes.Directory) is 0)
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
            throw new ArchiveException(ArchiveError.UnsafeEntry, $"The destination contains a reparse point: {path}",
                entryPath);
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
        => new(ArchiveError.UnsafeEntry, $"The archive path cannot be used as a Windows relative path: {entryPath}",
            entryPath);

    private static ArchiveException Exists(string path, string entryPath)
        => new(ArchiveError.DestinationExists, $"The destination already exists: {path}", entryPath);

    private static bool IsDeviceName(ReadOnlySpan<char> component)
    {
        var dot = component.IndexOf('.');
        var stem = (dot < 0 ? component : component[..dot]).TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                "0123456789¹²³".Contains(stem[3]));
    }
}
