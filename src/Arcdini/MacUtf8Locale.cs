using System.Runtime.InteropServices;

namespace Arcdini;

internal readonly partial struct MacUtf8Locale : IDisposable
{
    private const int CharacterTypeMask = 1 << 1; // Darwin LC_CTYPE_MASK
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private static readonly Lazy<nint> Utf8 = new(() =>
    {
        var locale = NewLocale(CharacterTypeMask, "en_US.UTF-8", 0);
        return locale != 0 ? locale : throw new IOException("Could not create the macOS UTF-8 locale.");
    });

    private readonly nint _previous;

    private MacUtf8Locale(nint previous)
    {
        _previous = previous;
    }

    internal static MacUtf8Locale Enter()
    {
        if (!OperatingSystem.IsMacOS())
            return default;

        // libarchive converts entry names using the current C locale.
        var previous = UseLocale(Utf8.Value);
        return previous != 0 ? new MacUtf8Locale(previous) : throw new IOException("Could not set the macOS UTF-8 locale.");
    }

    public void Dispose()
    {
        if (_previous != 0)
            UseLocale(_previous);
    }

    [LibraryImport(LibSystem, EntryPoint = "newlocale", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint NewLocale(int mask, string name, nint baseLocale);

    [LibraryImport(LibSystem, EntryPoint = "uselocale")]
    private static partial nint UseLocale(nint locale);
}
