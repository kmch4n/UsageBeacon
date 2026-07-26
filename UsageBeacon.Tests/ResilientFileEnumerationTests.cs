using UsageBeacon.Utilities;

namespace UsageBeacon.Tests;

public sealed class ResilientFileEnumerationTests
{
    [Fact]
    public void IgnoringFileSystemErrors_YieldsEveryItem_WhenTheSequenceCompletes()
    {
        var result = ResilientFileEnumeration
            .IgnoringFileSystemErrors(() => new[] { "a", "b", "c" }.AsEnumerable())
            .ToList();

        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void IgnoringFileSystemErrors_KeepsEarlierItems_WhenIterationThrowsUnauthorizedAccess()
    {
        var result = ResilientFileEnumeration
            .IgnoringFileSystemErrors(() => ThrowAfterTwoItems(new UnauthorizedAccessException()))
            .ToList();

        Assert.Equal(new[] { "first", "second" }, result);
    }

    [Fact]
    public void IgnoringFileSystemErrors_KeepsEarlierItems_WhenIterationThrowsIOException()
    {
        var result = ResilientFileEnumeration
            .IgnoringFileSystemErrors(() => ThrowAfterTwoItems(new IOException("share disconnected")))
            .ToList();

        Assert.Equal(new[] { "first", "second" }, result);
    }

    [Fact]
    public void IgnoringFileSystemErrors_ReturnsEmpty_WhenTheSequenceCannotBeCreated()
    {
        var result = ResilientFileEnumeration
            .IgnoringFileSystemErrors<string>(() => throw new IOException("unavailable"))
            .ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void IgnoringFileSystemErrors_Propagates_WhenTheFailureIsUnrelated()
    {
        var sequence = ResilientFileEnumeration
            .IgnoringFileSystemErrors(() => ThrowAfterTwoItems(new InvalidOperationException()));

        Assert.Throws<InvalidOperationException>(() => { sequence.ToList(); });
    }

    private static IEnumerable<string> ThrowAfterTwoItems(Exception exception)
    {
        yield return "first";
        yield return "second";
        throw exception;
    }
}
