# Arcdini implementation status

Updated November 23, 2026

The "Filedini performance improvements" discussion outlined a standalone .NET library and a Windows x64 NuGet package. Integration with Filedini was deferred to follow-up work.

- [x] Reviewed the discussion and the existing repository. The repository had an MIT license and no code.
- [x] Built libarchive without RAR or RAR5 support. The DLL depends on `bcrypt.dll` and `KERNEL32.dll`. Verified that the build excluded RAR sources and exports and that `Arcdini.0.1.0.nupkg` contained the DLL and notices.
- [x] Implemented archive listing, full and selective extraction, reading entry contents as streams, archive creation, encryption metadata, password support, progress reporting, cancellation, and typed errors. The Release build reported zero warnings and errors.
- [x] Ran the smoke test application against the same NuGet package on .NET 11 and win-x64 NativeAOT. Verified creation, listing, reading, selective extraction, and full extraction for seven formats, along with Japanese paths, encrypted ZIP archives, cancellation, and rejection of unsafe extraction paths.
- [x] Rebuilt the final NuGet package with fixes to the notices and file handling during extraction. Added empty archives for all seven formats. All checks passed on .NET 11 and NativeAOT.
- [x] Verified that all smoke checks for the final NuGet package also passed on .NET 8, the minimum supported target. Use `-p:SmokeTargetFramework=net8.0` to switch the smoke test target. Restoring again with network access resolved the NuGet audit warnings from the sandboxed restore.
- [x] Added xUnit unit tests. All 38 tests passed on Windows x64 with .NET 8. No tests failed or were skipped. Run them with `dotnet test --project tests/Arcdini.Tests -c Release`.
- [x] Added the library and both test projects to `Arcdini.slnx`. With the smoke test project pointed at a local package version that did not exist, `dotnet build -c Release` from the repository root reported zero warnings and errors, and `dotnet test -c Release` passed all 38 tests.
