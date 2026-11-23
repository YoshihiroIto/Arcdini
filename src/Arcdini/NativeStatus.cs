using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Arcdini;

internal class NativeStatus(CancellationToken cancellationToken)
{
    private ExceptionDispatchInfo? _callbackError;

    internal CancellationToken CancellationToken { get; } = cancellationToken;

    internal void RecordCallbackError(Exception error)
    {
        _callbackError ??= ExceptionDispatchInfo.Capture(error);
    }

    internal void Check(SafeHandle handle, long status, string operation, ArchiveError fallback, string? entryPath = null)
    {
        _callbackError?.Throw();
        CancellationToken.ThrowIfCancellationRequested();
        if (status >= Native.Ok)
            return;

        var detail = Marshal.PtrToStringUTF8(Native.ErrorString(handle)) ?? "Unknown libarchive error";
        var error = fallback;
        if (detail.Contains("Incorrect passphrase", StringComparison.OrdinalIgnoreCase))
            error = ArchiveError.IncorrectPassword;
        else if (detail.Contains("Passphrase required", StringComparison.OrdinalIgnoreCase))
            error = ArchiveError.PasswordRequired;
        else if (detail.Contains("Unrecognized archive format", StringComparison.OrdinalIgnoreCase))
            error = ArchiveError.UnsupportedFormat;
        else if (detail.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                 detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
            error = ArchiveError.UnsupportedFeature;

        throw new ArchiveException(error, $"{operation}: {detail}", entryPath, Native.ErrorNumber(handle));
    }

    internal static void ValidatePassword(string? password)
    {
        if (password is not null && (password.Length == 0 || password.Contains('\0')))
            throw new ArgumentException("A password must be nonempty and cannot contain NUL.", nameof(password));
    }

    internal static void CheckPlatform()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if ((OperatingSystem.IsWindows() && architecture == Architecture.X64) ||
            (OperatingSystem.IsMacOS() && architecture is Architecture.X64 or Architecture.Arm64))
            return;

        throw new PlatformNotSupportedException("Arcdini requires Windows x64 or macOS (x64 or arm64).");
    }
}
