using CliniMark.Presentation.Models;

namespace CliniMark.Tests;

public sealed class AnnotationProgressTests
{
    [Theory]
    [InlineData("5/5", true)]
    [InlineData("1/1", true)]
    [InlineData(" 3 / 3 ", true)]
    [InlineData("0/0", false)]
    [InlineData("0/5", false)]
    [InlineData("4/5", false)]
    [InlineData("6/5", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not a counter", false)]
    [InlineData("5/5/5", false)]
    public void CompletionRequiresAPositiveFullyCompletedQueue(string? counter, bool expected)
    {
        Assert.Equal(expected, AnnotationProgress.IsComplete(counter));
    }
}
