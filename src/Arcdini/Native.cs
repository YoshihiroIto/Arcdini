using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]

namespace Arcdini;

internal static unsafe partial class Native
{
    private const string Library = "arcdini-libarchive";

    internal const int Ok = 0;
    internal const int End = 1;
    internal const int Fatal = -30;
    internal const int FormatFamilyMask = 0xFF0000;
    internal const int TarFormatFamily = 0x030000;
    internal const ushort RegularFile = 0x8000;
    internal const ushort Directory = 0x4000;
    internal const ushort SymbolicLink = 0xA000;

    [LibraryImport(Library, EntryPoint = "archive_read_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ReadNew();

    [LibraryImport(Library, EntryPoint = "archive_read_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadFree(nint archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_format_zip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadZip(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_format_7zip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadSevenZip(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_format_tar")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadTar(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_filter_gzip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadGzip(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_filter_bzip2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadBzip2(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_filter_xz")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadXz(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_filter_lzip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadLzip(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_support_filter_zstd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadZstd(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_set_seek_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadSetSeek(ReadHandle archive,
        delegate* unmanaged[Cdecl]<nint, nint, long, int, long> callback);

    [LibraryImport(Library, EntryPoint = "archive_read_open2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadOpen(ReadHandle archive, nint context,
        nint open, delegate* unmanaged[Cdecl]<nint, nint, nint*, nint> read,
        delegate* unmanaged[Cdecl]<nint, nint, long, long> skip, nint close);

    [LibraryImport(Library, EntryPoint = "archive_read_next_header")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadNextHeader(ReadHandle archive, out nint entry);

    [LibraryImport(Library, EntryPoint = "archive_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadFormat(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_data_skip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadSkip(ReadHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_read_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ReadData(ReadHandle archive, byte* buffer, nuint length);

    [LibraryImport(Library, EntryPoint = "archive_read_add_passphrase", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ReadAddPassphrase(ReadHandle archive, string passphrase);

    [LibraryImport(Library, EntryPoint = "archive_write_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint WriteNew();

    [LibraryImport(Library, EntryPoint = "archive_write_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteFree(nint archive);

    [LibraryImport(Library, EntryPoint = "archive_write_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteClose(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_fail")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteFail(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_set_format_zip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteZip(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_set_format_7zip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteSevenZip(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_set_format_pax_restricted")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteTar(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_add_filter_gzip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteGzip(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_add_filter_bzip2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteBzip2(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_add_filter_xz")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteXz(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_add_filter_lzip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteLzip(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_add_filter_zstd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteZstd(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_write_set_format_option", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteFormatOption(WriteHandle archive, string module, string option, string value);

    [LibraryImport(Library, EntryPoint = "archive_write_set_passphrase", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteSetPassphrase(WriteHandle archive, string passphrase);

    [LibraryImport(Library, EntryPoint = "archive_write_set_bytes_in_last_block")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteLastBlock(WriteHandle archive, int bytes);

    [LibraryImport(Library, EntryPoint = "archive_write_open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteOpen(WriteHandle archive, nint context, nint open,
        delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, nint> write, nint close);

    [LibraryImport(Library, EntryPoint = "archive_write_header")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteHeader(WriteHandle archive, EntryHandle entry);

    [LibraryImport(Library, EntryPoint = "archive_write_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint WriteData(WriteHandle archive, byte* buffer, nuint length);

    [LibraryImport(Library, EntryPoint = "archive_write_finish_entry")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int WriteFinishEntry(WriteHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_entry_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint EntryNew();

    [LibraryImport(Library, EntryPoint = "archive_entry_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntryFree(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_pathname_w")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint EntryPathWide(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_symlink_w")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint EntrySymlinkWide(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_symlink_utf8")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint EntrySymlinkUtf8(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_pathname_utf8")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint EntryPathUtf8(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_filetype")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ushort EntryType(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_hardlink_is_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int EntryHasHardLink(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial long EntrySize(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_size_is_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int EntryHasSize(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_mtime")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial long EntryMTime(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_mtime_nsec")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial CLong EntryMTimeNanoseconds(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_mtime_is_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int EntryHasMTime(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_birthtime_is_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int EntryHasBirthTime(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_birthtime")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial long EntryBirthTime(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_birthtime_nsec")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial CLong EntryBirthTimeNanoseconds(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_atime_is_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int EntryHasATime(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_atime")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial long EntryATime(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_atime_nsec")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial CLong EntryATimeNanoseconds(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_is_encrypted")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int EntryEncrypted(nint entry);

    [LibraryImport(Library, EntryPoint = "archive_entry_copy_pathname_w", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetPathWide(EntryHandle entry, string path);

    [LibraryImport(Library, EntryPoint = "archive_entry_copy_symlink_w", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetSymlinkWide(EntryHandle entry, string path);

    [LibraryImport(Library, EntryPoint = "archive_entry_set_pathname_utf8", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetPathUtf8(EntryHandle entry, string path);

    [LibraryImport(Library, EntryPoint = "archive_entry_set_symlink_utf8", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetSymlinkUtf8(EntryHandle entry, string path);

    [LibraryImport(Library, EntryPoint = "archive_entry_set_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetSize(EntryHandle entry, long size);

    [LibraryImport(Library, EntryPoint = "archive_entry_set_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetMode(EntryHandle entry, ushort mode);

    [LibraryImport(Library, EntryPoint = "archive_entry_set_mtime")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EntrySetMTime(EntryHandle entry, long seconds, CLong nanoseconds);

    [LibraryImport(Library, EntryPoint = "archive_error_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ErrorString(SafeHandle archive);

    [LibraryImport(Library, EntryPoint = "archive_errno")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ErrorNumber(SafeHandle archive);
}