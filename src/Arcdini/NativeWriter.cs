using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arcdini;

internal sealed unsafe class NativeWriter : IDisposable
{
    private sealed class Output(Stream stream, CancellationToken cancellationToken) : NativeStatus(cancellationToken)
    {
        internal Stream Stream { get; } = stream;
    }

    private readonly WriteHandle _handle;
    private readonly Output _output;
    private bool _closed;

    internal NativeWriter(Stream destination, ArchiveCreationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var locale = MacUtf8Locale.Enter();
        _output = new Output(destination, cancellationToken);
        _handle = new WriteHandle();
        try
        {
            var format = options.Format switch
            {
                ArchiveFormat.Zip => Native.WriteZip(_handle),
                ArchiveFormat.SevenZip => Native.WriteSevenZip(_handle),
                _ => Native.WriteTar(_handle)
            };
            Check(format, "Set archive format");

            var filter = options.Format switch
            {
                ArchiveFormat.TarGzip => Native.WriteGzip(_handle),
                ArchiveFormat.TarBzip2 => Native.WriteBzip2(_handle),
                ArchiveFormat.TarXz => Native.WriteXz(_handle),
                ArchiveFormat.TarLzip => Native.WriteLzip(_handle),
                ArchiveFormat.TarZstandard => Native.WriteZstd(_handle),
                _ => Native.Ok
            };
            Check(filter, "Set archive compression");

            if (options.Format == ArchiveFormat.Zip)
                Check(Native.WriteFormatOption(_handle, "zip", "hdrcharset", "UTF-8"), "Set ZIP filename encoding");

            if (options.Password is { } password)
            {
                Check(Native.WriteSetPassphrase(_handle, password), "Set ZIP passphrase");
                Check(Native.WriteFormatOption(_handle, "zip", "encryption", "aes256"),
                    "Enable ZIP AES-256 encryption");
            }

            Check(Native.WriteLastBlock(_handle, 1), "Set archive block padding");
            Check(Native.WriteOpen(_handle, _handle.SetContext(_output), 0, &WriteOutput, 0), "Open archive writer");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void WriteHeader(ArchiveEntrySource source, long size, DateTimeOffset? lastWriteTime)
    {
        using var locale = MacUtf8Locale.Enter();
        using var entry = new EntryHandle();
        var path = source.IsDirectory ? source.Path + "/" : source.Path;
        if (OperatingSystem.IsWindows())
            Native.EntrySetPathWide(entry, path);
        else
            Native.EntrySetPathUtf8(entry, path);
        Native.EntrySetSize(entry, size);
        Native.EntrySetMode(entry, source.IsDirectory ? (ushort)0x41ED :
            source.LinkTarget is null ? (ushort)0x81A4 : (ushort)0xA1FF);
        if (source.LinkTarget is { } linkTarget)
        {
            if (OperatingSystem.IsWindows())
                Native.EntrySetSymlinkWide(entry, linkTarget);
            else
                Native.EntrySetSymlinkUtf8(entry, linkTarget);
        }

        if (lastWriteTime is { } time)
        {
            var seconds = time.ToUnixTimeSeconds();
            var fraction = time.UtcTicks - DateTimeOffset.FromUnixTimeSeconds(seconds).UtcTicks;
            Native.EntrySetMTime(entry, seconds, new CLong(checked((int)(fraction * 100))));
        }

        Check(Native.WriteHeader(_handle, entry), "Write archive entry header", source.Path);
    }

    internal void Write(ReadOnlySpan<byte> data, string entryPath)
    {
        _output.CancellationToken.ThrowIfCancellationRequested();
        fixed (byte* pointer = data)
        {
            var written = Native.WriteData(_handle, pointer, (nuint)data.Length);
            Check(written, "Write archive entry data", entryPath);
            if (written != data.Length)
                throw new ArchiveException(ArchiveError.NativeFailure, "libarchive did not accept all entry data.",
                    entryPath);
        }
    }

    internal void FinishEntry(string path)
        => Check(Native.WriteFinishEntry(_handle), "Finish archive entry", path);

    internal void Complete()
    {
        using var locale = MacUtf8Locale.Enter();
        Check(Native.WriteClose(_handle), "Finish archive");
        _closed = true;
    }

    public void Dispose()
    {
        if (_handle.IsClosed)
            return;
        if (!_closed)
            Native.WriteFail(_handle);
        _handle.Dispose();
    }

    private void Check(long status, string operation, string? entryPath = null)
        => _output.Check(_handle, status, operation, ArchiveError.NativeFailure, entryPath);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint WriteOutput(nint archive, nint context, byte* buffer, nuint length)
    {
        var output = (Output)GCHandle.FromIntPtr(context).Target!;
        try
        {
            output.CancellationToken.ThrowIfCancellationRequested();
            output.Stream.Write(new ReadOnlySpan<byte>(buffer, checked((int)length)));
            return checked((nint)length);
        }
        catch (Exception error)
        {
            output.RecordCallbackError(error);
            return -1;
        }
    }
}