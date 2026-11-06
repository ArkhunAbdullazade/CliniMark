using System.Text.Encodings.Web;
using System.Text.Json;
using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Presentation.Migrations;
using CliniMark.Presentation.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CliniMark.Tests;

public sealed class SourceUrlTests
{
    private const string SourceUrl = "https://www.nejm.org/image-challenge?ci=20261022&query=test";

    [Theory]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    public void LegacySourceSuffixIsSeparatedWithoutChangingDescription(string separator)
    {
        const string description = "A lesion.\nOriginal punctuation and spacing  ";
        var result = MedDataDescription.SplitLegacySource(description + separator + "Source: " + SourceUrl);
        Assert.Equal(description, result.Description);
        Assert.Equal(SourceUrl, result.SourceUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A plain description.\nSecond line.")]
    [InlineData("A source-looking phrase: Source: https://www.nejm.org/image-challenge?ci=20261022")]
    [InlineData("A description.\n\nSource: https://example.net/reference")]
    [InlineData("A description.\n\nSource: https://www.nejm.org/image-challenge?ci=20261022\nMore description.")]
    public void UnrelatedDescriptionTextAndUnknownValuesArePreserved(string? description)
    {
        var result = MedDataDescription.SplitLegacySource(description);
        Assert.Equal(description, result.Description);
        Assert.Null(result.SourceUrl);
    }

    [Fact]
    public void HighlightingOmitsLegacySourceAndKeepsDescriptionAndKeyword()
    {
        using var writer = new StringWriter();
        DescriptionKeywords.Highlight("A <lesion>.\n\nSource: " + SourceUrl, ["lesion"])
            .WriteTo(writer, HtmlEncoder.Default);
        var html = writer.ToString();
        Assert.Contains("data-keyword=\"lesion\"", html);
        Assert.Contains("&lt;", html);
        Assert.DoesNotContain("Source:", html);
        Assert.DoesNotContain("nejm.org", html);
        Assert.Equal("A <lesion>.", System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "")));
    }

    [Fact]
    public void BrowserImagePayloadHasOnlyIdentityAndImageUrl()
    {
        var image = new MedData
        {
            Id = 5, ImageUrl = "https://images.example.test/5.jpg", SourceUrl = SourceUrl,
            ImageDescription = "A lesion.\n\nSource: " + SourceUrl,
            Diagnosis = "Unchanged diagnosis", Pmcid = "NEJM:20261022:image-1",
            MedDataKeywords = [new() { Keyword = "lesion" }]
        };
        var json = JsonSerializer.Serialize(AnnotationImage.From(image), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(["id", "imageUrl"], document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(5, document.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(image.ImageUrl, document.RootElement.GetProperty("imageUrl").GetString());
        Assert.DoesNotContain("nejm.org", json);
        Assert.Equal("Unchanged diagnosis", image.Diagnosis);
        Assert.Equal(SourceUrl, image.SourceUrl);
        Assert.Null(AnnotationImage.From(null));
    }

    [Fact]
    public void SourceUrlMigrationOnlyAddsOneNullableColumn()
    {
        var operation = Assert.IsType<AddColumnOperation>(Assert.Single(new AddMedDataSourceUrl().UpOperations));
        Assert.Equal("MedDatas", operation.Table);
        Assert.Equal("SourceUrl", operation.Name);
        Assert.Equal("text", operation.ColumnType);
        Assert.True(operation.IsNullable);
        using var context = new MedDataDbContext(new DbContextOptionsBuilder<MedDataDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        Assert.True(context.Model.FindEntityType(typeof(MedData))!.FindProperty(nameof(MedData.SourceUrl))!.IsNullable);
    }
}
