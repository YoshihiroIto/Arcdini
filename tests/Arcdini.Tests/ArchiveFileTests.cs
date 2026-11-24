namespace Arcdini.Tests;

public sealed class ArchiveFileTests
{
    [Theory]
    [InlineData("")]
    [InlineData("some\0password")]
    public void CreateRejectsInvalidPasswordBeforeWriting(string password)
    {
        using var destination = new MemoryStream();
        var options = new ArchiveCreationOptions(ArchiveFormat.Zip) { Password = password };

        Assert.Throws<ArgumentException>("password", () => ArchiveFile.Create(destination, [], options,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [InlineData(ArchiveFormat.SevenZip)]
    [InlineData(ArchiveFormat.TarGzip)]
    public void CreateRejectsEncryptionForNonZipFormats(ArchiveFormat format)
    {
        using var destination = new MemoryStream();
        var options = new ArchiveCreationOptions(format) { Password = "password" };

        var error = Assert.Throws<ArchiveException>(() => ArchiveFile.Create(destination, [], options,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ArchiveError.UnsupportedFeature, error.Error);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void CreateRejectsUnknownFormatBeforeWriting()
    {
        using var destination = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>("options",
            () => ArchiveFile.Create(destination, [], new((ArchiveFormat)123),
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void CreateRejectsReadOnlyDestination()
    {
        using var destination = new MemoryStream([], writable: false);

        Assert.Throws<ArgumentException>("destination",
            () => ArchiveFile.Create(destination, [], new(ArchiveFormat.Zip),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CancelledCreationLeavesDestinationUntouched()
    {
        using var destination = new MemoryStream();
        var token = new CancellationToken(canceled: true);

        var error = Assert.Throws<OperationCanceledException>(() =>
            ArchiveFile.Create(destination, [], new(ArchiveFormat.Zip), leaveOpen: true, cancellationToken: token));

        Assert.Equal(token, error.CancellationToken);
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisposeHonorsStreamOwnership(bool leaveOpen)
    {
        using var source = new MemoryStream();
        using (ArchiveFile.Open(source, leaveOpen: leaveOpen, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.True(source.CanRead);
        }

        Assert.Equal(leaveOpen, source.CanRead);
    }

    [Fact]
    public void DisposedArchiveRejectsListingBeforeReading()
    {
        using var source = new MemoryStream();
        var archive = ArchiveFile.Open(source, leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);
        archive.Dispose();

        Assert.Throws<ObjectDisposedException>(() => archive.GetEntries(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void OpenEntryRejectsNegativeIdBeforeReading()
    {
        using var archive =
            ArchiveFile.Open(new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Throws<ArgumentOutOfRangeException>("entryId",
            () => archive.OpenEntry(-1, TestContext.Current.CancellationToken));
    }
}