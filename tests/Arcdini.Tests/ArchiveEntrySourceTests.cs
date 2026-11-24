namespace Arcdini.Tests;

public sealed class ArchiveEntrySourceTests
{
    [Fact]
    public void StreamSourcePreservesSizeAndTimestampAndOpensLazily()
    {
        var timestamp = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
        var calls = 0;
        var entry = ArchiveEntrySource.FromStream(@"./資料\report.txt", 3, () =>
        {
            calls++;
            return new MemoryStream([1, 2, 3]);
        }, timestamp);

        Assert.Equal("資料/report.txt", entry.Path);
        Assert.False(entry.IsDirectory);
        Assert.Equal(timestamp, entry.GetLastWriteTime());
        Assert.Equal(0, calls);

        using var content = entry.OpenContent();
        Assert.NotNull(content);
        Assert.Equal(3L, entry.GetSize(content));
        Assert.Equal(1, content.ReadByte());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DirectoryHasNoContentAndRetainsItsTimestamp()
    {
        var timestamp = DateTimeOffset.UnixEpoch;
        var entry = ArchiveEntrySource.Directory("parent//child/", timestamp);

        Assert.Equal("parent/child", entry.Path);
        Assert.True(entry.IsDirectory);
        Assert.Null(entry.OpenContent());
        Assert.Equal(0L, entry.GetSize(null));
        Assert.Equal(timestamp, entry.GetLastWriteTime());
    }

    [Fact]
    public void FileSourceDefaultsToBasename()
    {
        var entry = ArchiveEntrySource.FromFile(Path.Combine("external", "日本語.txt"));

        Assert.Equal("日本語.txt", entry.Path);
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void RootMarkerCannotBeUsedAsNewEntry()
    {
        var error = Assert.Throws<ArchiveException>(() => ArchiveEntrySource.Directory("./"));

        Assert.Equal(ArchiveError.UnsafeEntry, error.Error);
    }

    [Fact]
    public void StreamSourceRejectsNegativeSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>("size",
            () => ArchiveEntrySource.FromStream("file", -1, () => Stream.Null));
    }

    [Fact]
    public void StreamSourceRejectsMissingFactory()
    {
        Assert.Throws<ArgumentNullException>("openStream",
            () => ArchiveEntrySource.FromStream("file", 0, null!));
    }

    [Fact]
    public void StreamSourceRejectsFactoryReturningNullOnOpen()
    {
        var entry = ArchiveEntrySource.FromStream("file", 0, () => null!);

        Assert.Throws<InvalidOperationException>(() => entry.OpenContent());
    }
}