namespace Arcdini.Tests;

public sealed class ArchivePathTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/absolute.txt")]
    [InlineData(@"\absolute.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData("C:relative.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("../outside.txt")]
    [InlineData(@"folder\..\outside.txt")]
    [InlineData("folder/file.txt:stream")]
    [InlineData("folder./file.txt")]
    [InlineData("folder /file.txt")]
    [InlineData("file\u0001.txt")]
    [InlineData("bad?.txt")]
    [InlineData("folder/nUl.txt")]
    [InlineData("CON .txt")]
    [InlineData("COM¹.log")]
    [InlineData("LPT9")]
    public void UnsafePathReportsOriginalEntry(string path)
    {
        var error = Assert.Throws<ArchiveException>(() => ArchivePaths.ValidateEntryPath(path));

        Assert.Equal(ArchiveError.UnsafeEntry, error.Error);
        Assert.Equal(path, error.EntryPath);
    }

    [Fact]
    public void RelativePathAcceptsMixedSeparatorsAndPreservesUnicode()
    {
        var components = ArchivePaths.ValidateEntryPath(@"./資料\./🌐//日本語.txt");

        Assert.Equal(["資料", "🌐", "日本語.txt"], components);
    }

    [Fact]
    public void SimilarNamesAreNotWindowsDevices()
    {
        Assert.Equal(["COM10", "NUL-report.txt", ".gitignore"],
            ArchivePaths.ValidateEntryPath("COM10/NUL-report.txt/.gitignore"));
    }

    [Fact]
    public void TarRootDirectoryHasNoRelativeComponents()
    {
        Assert.Empty(ArchivePaths.ValidateEntryPath("./"));
    }
}
