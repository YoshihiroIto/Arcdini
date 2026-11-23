using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Arcdini;

internal abstract class CallbackHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private GCHandle _context;

    protected CallbackHandle(nint pointer) : base(ownsHandle: true)
    {
        SetHandle(pointer);
        if (IsInvalid)
            throw new ArchiveException(ArchiveError.NativeFailure, "libarchive could not allocate a handle.");
    }

    internal nint SetContext(object state)
    {
        _context = GCHandle.Alloc(state);
        return GCHandle.ToIntPtr(_context);
    }

    protected sealed override bool ReleaseHandle()
    {
        try
        {
            return Free(handle) >= Native.Ok;
        }
        finally
        {
            if (_context.IsAllocated)
                _context.Free();
        }
    }

    protected abstract int Free(nint pointer);
}

internal sealed class ReadHandle() : CallbackHandle(Native.ReadNew())
{
    protected override int Free(nint pointer) => Native.ReadFree(pointer);
}

internal sealed class WriteHandle() : CallbackHandle(Native.WriteNew())
{
    protected override int Free(nint pointer) => Native.WriteFree(pointer);
}

internal sealed class EntryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal EntryHandle() : base(ownsHandle: true)
    {
        SetHandle(Native.EntryNew());
        if (IsInvalid)
            throw new ArchiveException(ArchiveError.NativeFailure, "libarchive could not allocate an entry.");
    }

    protected override bool ReleaseHandle()
    {
        Native.EntryFree(handle);
        return true;
    }
}
