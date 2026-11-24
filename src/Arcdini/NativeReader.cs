using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arcdini;

internal sealed unsafe class NativeReader : IDisposable
{
    private sealed class Input(Stream stream, long origin, CancellationToken cancellationToken)
        : NativeStatus(cancellationToken)
    {
        internal Stream Stream { get; } = stream;
        internal long Origin { get; } = origin;
        internal byte[] Buffer { get; } = GC.AllocateUninitializedArray<byte>(128 * 1024, pinned: true);
    }

    private readonly ReadHandle _handle;
    private readonly Input _input;
    private long _nextId;
    private nint _entry;
    internal ArchiveEntry? Current { get; private set; }
    internal string? LinkTarget { get; private set; }

    internal NativeReader(Stream source, long origin, string? password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var locale = MacUtf8Locale.Enter();
        _input = new Input(source, origin, cancellationToken);
        _handle = new ReadHandle();
        try
        {
            Check(Native.ReadZip(_handle), "Enable ZIP");
            Check(Native.ReadSevenZip(_handle), "Enable 7z");
            Check(Native.ReadTar(_handle), "Enable TAR");
            Check(Native.ReadGzip(_handle), "Enable gzip");
            Check(Native.ReadBzip2(_handle), "Enable bzip2");
            Check(Native.ReadXz(_handle), "Enable xz");
            Check(Native.ReadLzip(_handle), "Enable lzip");
            Check(Native.ReadZstd(_handle), "Enable Zstandard");
            if (password is { })
                Check(Native.ReadAddPassphrase(_handle, password), "Set passphrase");
            Check(Native.ReadSetSeek(_handle, &Seek), "Set the archive seek callback");
            Check(Native.ReadOpen(_handle, _handle.SetContext(_input), 0, &ReadInput, &SkipInput, 0), "Open archive");
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    internal bool MoveNext()
    {
        _input.CancellationToken.ThrowIfCancellationRequested();
        using var locale = MacUtf8Locale.Enter();
        Current = null;
        var status = Native.ReadNextHeader(_handle, out _entry);
        Check(status, "Read entry header");
        if (status == Native.End)
            return false;

        var entry = _entry;
        var path = OperatingSystem.IsWindows()
            ? Marshal.PtrToStringUni(Native.EntryPathWide(entry)) ??
              Marshal.PtrToStringUTF8(Native.EntryPathUtf8(entry))
            : Marshal.PtrToStringUTF8(Native.EntryPathUtf8(entry));
        if (string.IsNullOrEmpty(path))
            throw new ArchiveException(ArchiveError.InvalidArchive, "Could not decode the entry path.");

        var kind = Native.EntryType(entry) switch
        {
            _ when Native.EntryHasHardLink(entry) != 0 => ArchiveEntryKind.HardLink,
            Native.RegularFile => ArchiveEntryKind.File,
            Native.Directory => ArchiveEntryKind.Directory,
            Native.SymbolicLink => ArchiveEntryKind.SymbolicLink,
            _ => ArchiveEntryKind.Other
        };
        long? size = Native.EntryHasSize(entry) != 0 ? Native.EntrySize(entry) : null;
        if (kind == ArchiveEntryKind.File && size is 0 &&
            (Native.ReadFormat(_handle) & Native.FormatFamilyMask) == Native.TarFormatFamily)
        {
            var directoryPath = path.TrimEnd();
            if (directoryPath.EndsWith('/') || directoryPath.EndsWith('\\'))
            {
                path = directoryPath.TrimEnd('/', '\\').TrimEnd() + "/";
                kind = ArchiveEntryKind.Directory;
            }
        }

        LinkTarget = kind == ArchiveEntryKind.SymbolicLink
            ? OperatingSystem.IsWindows()
                ? Marshal.PtrToStringUni(Native.EntrySymlinkWide(entry)) ??
                  Marshal.PtrToStringUTF8(Native.EntrySymlinkUtf8(entry))
                : Marshal.PtrToStringUTF8(Native.EntrySymlinkUtf8(entry))
            : null;
        Current = new ArchiveEntry(_nextId++, path, kind, size, GetLastWriteTime(entry),
            Native.EntryEncrypted(entry) != 0);
        return true;
    }

    internal void Skip()
    {
        _input.CancellationToken.ThrowIfCancellationRequested();
        Check(Native.ReadSkip(_handle), "Skip entry");
    }

    internal int Read(Span<byte> buffer)
    {
        _input.CancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
            return 0;

        fixed (byte* pointer = buffer)
        {
            var read = Native.ReadData(_handle, pointer, (nuint)buffer.Length);
            Check(read, "Read entry data");
            return checked((int)read);
        }
    }

    public void Dispose() => _handle.Dispose();

    private void Check(long status, string operation)
        => _input.Check(_handle, status, operation, ArchiveError.InvalidArchive, Current?.Path);

    private static DateTimeOffset? GetLastWriteTime(nint entry)
        => Native.EntryHasMTime(entry) is 0
            ? null
            : ReadTime(Native.EntryMTime(entry), Native.EntryMTimeNanoseconds(entry));

    internal (DateTimeOffset? Created, DateTimeOffset? Accessed) GetCreationAndAccessTimes()
        => (
            Native.EntryHasBirthTime(_entry) is 0
                ? null
                : ReadTime(Native.EntryBirthTime(_entry), Native.EntryBirthTimeNanoseconds(_entry)),
            Native.EntryHasATime(_entry) is 0
                ? null
                : ReadTime(Native.EntryATime(_entry), Native.EntryATimeNanoseconds(_entry)));

    private static DateTimeOffset? ReadTime(long seconds, CLong nanoseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds.Value / 100);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ReadInput(nint archive, nint context, nint* buffer)
    {
        var input = (Input)GCHandle.FromIntPtr(context).Target!;
        try
        {
            input.CancellationToken.ThrowIfCancellationRequested();
            fixed (byte* pointer = input.Buffer)
            {
                *buffer = (nint)pointer;
                return input.Stream.Read(input.Buffer);
            }
        }
        catch (Exception error)
        {
            *buffer = 0;
            input.RecordCallbackError(error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long SkipInput(nint archive, nint context, long length)
    {
        var input = (Input)GCHandle.FromIntPtr(context).Target!;
        try
        {
            input.CancellationToken.ThrowIfCancellationRequested();
            var position = input.Stream.Position;
            var count = Math.Clamp(length, 0, Math.Max(0, input.Stream.Length - position));
            return input.Stream.Seek(count, SeekOrigin.Current) - position;
        }
        catch (Exception error)
        {
            input.RecordCallbackError(error);
            return Native.Fatal;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Seek(nint archive, nint context, long offset, int origin)
    {
        var input = (Input)GCHandle.FromIntPtr(context).Target!;
        try
        {
            input.CancellationToken.ThrowIfCancellationRequested();
            long position = origin switch
            {
                0 => checked(input.Origin + offset),
                1 => checked(input.Stream.Position + offset),
                2 => checked(input.Stream.Length + offset),
                _ => -1
            };
            if (position < input.Origin || position > input.Stream.Length)
                return Native.Fatal;
            return input.Stream.Seek(position, SeekOrigin.Begin) - input.Origin;
        }
        catch (OverflowException)
        {
            return Native.Fatal;
        }
        catch (Exception error)
        {
            input.RecordCallbackError(error);
            return Native.Fatal;
        }
    }
}