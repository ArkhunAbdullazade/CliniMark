using CliniMark.Core.Models;
using CliniMark.Presentation.Dtos;

namespace CliniMark.Tests;

// Answers that pass the label check, for tests that are about something else.
internal static class LabelChecks
{
    public static readonly IReadOnlyList<MetadataAnswer> Answered =
        MetadataCheck.Fields.Select(field => new MetadataAnswer(field, MetadataCheck.CannotTell, null, false)).ToList();
}

public sealed class MetadataCheckTests
{
    private static MetadataCheckDto Corrected(string field, string? value) =>
        new() { Field = field, Verdict = MetadataCheck.Corrected, Value = value };

    private static MetadataAnswer ReadOne(MetadataCheckDto check)
    {
        Assert.True(MetadataCheckOptions.TryRead([check], out var answers, out _));
        return Assert.Single(answers);
    }

    [Theory]
    [InlineData(MetadataCheck.Modality, " ct ", "CT")]
    [InlineData(MetadataCheck.Speciality, "gastroenterology/HEPATOLOGY", "Gastroenterology/Hepatology")]
    [InlineData(MetadataCheck.BodyRegion, "chest  and   thorax", "Chest and Thorax")]
    public void AListedValueTakesTheListsSpelling(string field, string typed, string stored)
    {
        var answer = ReadOne(Corrected(field, typed));
        Assert.Equal(stored, answer.CorrectedValue);
        Assert.False(answer.IsNewValue);
    }

    [Fact]
    public void AValueOutsideTheListsIsMarkedAsNew()
    {
        var answer = ReadOne(Corrected(MetadataCheck.Speciality, "  Rheumatology "));
        Assert.Equal("Rheumatology", answer.CorrectedValue);
        Assert.True(answer.IsNewValue);
    }

    [Fact]
    public void OnlyACorrectionKeepsAValue()
    {
        var answer = ReadOne(new MetadataCheckDto { Field = MetadataCheck.Modality, Verdict = MetadataCheck.Correct, Value = "CT" });
        Assert.Null(answer.CorrectedValue);
        Assert.False(answer.IsNewValue);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("unknown")]
    public void ACorrectionNeedsARealValue(string? value) =>
        Assert.False(MetadataCheckOptions.TryRead([Corrected(MetadataCheck.BodyRegion, value)], out _, out _));

    [Fact]
    public void AnOverlongValueIsRefused() =>
        Assert.False(MetadataCheckOptions.TryRead([Corrected(MetadataCheck.Speciality, new string('a', MetadataCheck.MaxValueLength + 1))], out _, out _));

    [Fact]
    public void UnknownFieldsVerdictsRepeatsAndEmptyListsAreRefused()
    {
        Assert.False(MetadataCheckOptions.TryRead([new MetadataCheckDto { Field = "diagnosis", Verdict = MetadataCheck.Correct }], out _, out _));
        Assert.False(MetadataCheckOptions.TryRead([new MetadataCheckDto { Field = MetadataCheck.Modality, Verdict = "maybe" }], out _, out _));
        var same = new MetadataCheckDto { Field = MetadataCheck.Modality, Verdict = MetadataCheck.Correct };
        Assert.False(MetadataCheckOptions.TryRead([same, same], out _, out _));
        Assert.False(MetadataCheckOptions.TryRead([], out _, out _));
        Assert.False(MetadataCheckOptions.TryRead(null, out _, out _));
    }

    [Fact]
    public void TheSearchListsLeaveOutUnknown()
    {
        foreach (var field in MetadataCheck.Fields)
        {
            Assert.NotEmpty(MetadataCheckOptions.For(field));
            Assert.DoesNotContain("Unknown", MetadataCheckOptions.For(field));
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("u", true)]
    [InlineData("na", true)]
    [InlineData("Unknown", true)]
    [InlineData("CT", false)]
    public void ALabelIsMissingWhenThereIsNothingToConfirm(string? label, bool missing) =>
        Assert.Equal(missing, MetadataCheck.IsMissing(label));
}
