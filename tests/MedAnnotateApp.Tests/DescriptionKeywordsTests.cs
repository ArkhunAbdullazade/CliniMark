using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using MedAnnotateApp.Presentation.Models;

namespace MedAnnotateApp.Tests;

public sealed class DescriptionKeywordsTests
{
    [Theory]
    [InlineData("A lesion with a clear margin.", new[] { "lesion", "margin" }, new string[0])]
    [InlineData("A Lesion here.", new[] { "lesion" }, new string[0])]
    [InlineData("A lesion here.", new[] { "lesion", "nodule" }, new[] { "nodule" })]
    [InlineData("A lung mass here.", new[] { "lung", "lung mass" }, new[] { "lung" })]
    [InlineData("A lung mass and the lung.", new[] { "lung", "lung mass" }, new string[0])]
    [InlineData("The lung here.", new[] { "lung", "Lung" }, new[] { "Lung" })]
    [InlineData(null, new[] { "lesion" }, new[] { "lesion" })]
    public void UnmatchedTermsListWhatThePageCouldNeverOffer(string? description, string[] terms, string[] expected) =>
        Assert.Equal(expected, DescriptionKeywords.UnmatchedTerms(description, terms));

    [Fact]
    public void TextAfterTheLastTermIsKept()
    {
        var html = Render("Before lesion after.", ["lesion"]);
        Assert.StartsWith("Before <span", html);
        Assert.EndsWith("lesion</span> after.", html);
    }


    [Fact]
    public void TermsStayInTheirOriginalTextAndFollowDescriptionOrder()
    {
        var html = Render("A lesion beside a boundary.", ["boundary", "lesion"]);
        Assert.Equal(["lesion", "boundary"], Keywords(html));
        Assert.Equal("A lesion beside a boundary.", PlainText(html));
    }

    [Fact]
    public void RepeatedTermIsHighlightedOnlyAtItsFirstOccurrence()
    {
        var html = Render("Lesion and LESION, then a lesion again", ["lesion"]);
        Assert.Equal(["lesion"], Keywords(html));
        Assert.Contains(">Lesion</span>", html);
        Assert.Equal("Lesion and LESION, then a lesion again", PlainText(html));
        Assert.Single(Regex.Matches(html, "id=\"keyword-[^\"]+\""));
    }

    [Fact]
    public void EachOfSeveralRepeatedTermsIsHighlightedOnce()
    {
        var html = Render("A mass near the boundary; the mass crosses the boundary.", ["mass", "boundary"]);
        Assert.Equal(["mass", "boundary"], Keywords(html));
        Assert.Equal("A mass near the boundary; the mass crosses the boundary.", PlainText(html));
    }

    [Fact]
    public void OverlapUsesLongestPhraseAndCanFindShorterTermLater()
    {
        var html = Render("large mass and mass", ["mass", "large mass"]);
        Assert.Equal(["large mass", "mass"], Keywords(html));
        Assert.Equal("large mass and mass", PlainText(html));
    }

    [Fact]
    public void DescriptionAndCanonicalTermAreEncodedInsteadOfBecomingMarkup()
    {
        var html = Render("<script>alert(1)</script> and a \"mass\"", ["\"mass\""]);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal(["\"mass\""], Keywords(html));
        Assert.Equal("<script>alert(1)</script> and a \"mass\"", PlainText(html));
    }

    [Fact]
    public void UnknownMissingAndDuplicateTermsDoNotInventTextOrResponses()
    {
        Assert.Empty(Keywords(Render(null, ["mass"])));
        Assert.Empty(Keywords(Render("Source description", ["missing"])));
        Assert.Equal(["mass"], Keywords(Render("mass", [null, "", "mass", "mass"])));
    }

    private static string Render(string? text, IReadOnlyList<string?> terms)
    {
        using var writer = new StringWriter();
        DescriptionKeywords.Highlight(text, terms).WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static string[] Keywords(string html) => Regex.Matches(html, "data-keyword=\"([^\"]*)\"")
        .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();

    private static string PlainText(string html) => System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", ""));
}
