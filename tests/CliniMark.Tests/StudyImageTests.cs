using System.Net;
using System.Reflection;
using CliniMark.Core.Models;
using CliniMark.Core.Services;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Services;
using CliniMark.Presentation.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Tests;

public sealed class StudyImageTests
{
    private const string File = "1ad0aaf1cd0cb2329549d6c08864f341fde740286018ef818efbd9afce226d1f.webp";
    private const string Stored = "https://studyimages.blob.core.windows.net/images/" + File;

    [Fact]
    public void ImagesInBlobStorageAreShownThroughTheAppAndEverythingElseIsLeftAlone()
    {
        Assert.Equal($"/study-images/42/{File}", StudyImageAddress.For(42, Stored));
        Assert.Equal($"/study-images/42/{File}", StudyImageAddress.For(42, "  " + Stored + " "));
        foreach (var other in new[]
                 {
                     "/images/samples/2000451.png", "https://example.org/figure.png", "http://studyimages.blob.core.windows.net/images/a.webp",
                     Stored + "?sv=2024&sig=secret", Stored + "#part", "https://studyimages.blob.core.windows.net:8443/images/a.webp",
                     "https://studyimages.blob.core.windows.net.example.org/images/a.webp", "https://studyimages.blob.core.windows.net/images/",
                     "https://studyimages.blob.core.windows.net/images/a%20b.webp", "not a url", ""
                 })
            Assert.Equal(other, StudyImageAddress.For(42, other));
        Assert.Null(StudyImageAddress.For(42, null));
    }

    [Fact]
    public async Task TheProxyFetchesOnlyTheStoredAddressOfThatImage()
    {
        await using var db = await Db.CreateAsync((1, Stored), (2, "/images/samples/local.png"), (3, Stored.Replace(File, "other.webp")));
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = Image([1, 2, 3, 4]) });
        var proxy = new StudyImageProxy(db.Context, new HttpClient(handler));

        var (outcome, image) = await proxy.OpenAsync(1, File, CancellationToken.None);
        Assert.Equal(StudyImageProxy.Outcome.Found, outcome);
        await using (image)
        {
            Assert.Equal(("image/webp", 4L), (image!.ContentType, image.Length));
            using var copy = new MemoryStream();
            await image.Content.CopyToAsync(copy);
            Assert.Equal([1, 2, 3, 4], copy.ToArray());
        }
        Assert.Equal([Stored], handler.Requests);

        // A file name that isn't this image's, an unknown image, or an image not in blob storage: refused without a request.
        Assert.Equal(StudyImageProxy.Outcome.NotFound, (await proxy.OpenAsync(3, File, CancellationToken.None)).Outcome);
        Assert.Equal(StudyImageProxy.Outcome.NotFound, (await proxy.OpenAsync(99, File, CancellationToken.None)).Outcome);
        Assert.Equal(StudyImageProxy.Outcome.NotFound, (await proxy.OpenAsync(2, "local.png", CancellationToken.None)).Outcome);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/webp", StudyImageProxy.Outcome.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, "image/webp", StudyImageProxy.Outcome.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, "application/xml", StudyImageProxy.Outcome.Unavailable)]
    [InlineData(HttpStatusCode.OK, "text/html", StudyImageProxy.Outcome.Unavailable)]
    public async Task StorageTroubleIsReportedAndNeverPassedOn(HttpStatusCode status, string type, StudyImageProxy.Outcome expected)
    {
        await using var db = await Db.CreateAsync((1, Stored));
        var content = new ByteArrayContent([9]);
        content.Headers.ContentType = new(type);
        var proxy = new StudyImageProxy(db.Context, new HttpClient(new Handler(_ => new HttpResponseMessage(status) { Content = content })));

        var (outcome, image) = await proxy.OpenAsync(1, File, CancellationToken.None);

        Assert.Equal(expected, outcome);
        Assert.Null(image);
    }

    [Fact]
    public async Task AStorageThatCannotBeReachedIsReportedAsUnavailable()
    {
        await using var db = await Db.CreateAsync((1, Stored));
        var proxy = new StudyImageProxy(db.Context, new HttpClient(new Handler(_ => throw new HttpRequestException("No route to host"))));

        Assert.Equal(StudyImageProxy.Outcome.Unavailable, (await proxy.OpenAsync(1, File, CancellationToken.None)).Outcome);
    }

    [Fact]
    public void TheImageRouteNeedsASignedInAccountAndIsNeverCachedByOthers()
    {
        Assert.NotNull(typeof(StudyImagesController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(StudyImagesController).GetMethod(nameof(StudyImagesController.Get))!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("study-images", typeof(StudyImagesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.StartsWith("private", StudyImagesController.CacheControl);
        Assert.Equal(StudyImageAddress.RoutePrefix.TrimStart('/'), typeof(StudyImagesController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    private static ByteArrayContent Image(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/webp");
        return content;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Db : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MedDataDbContext Context { get; }

        private Db(SqliteConnection connection)
        {
            this.connection = connection;
            Context = new MedDataDbContext(new DbContextOptionsBuilder<MedDataDbContext>().UseSqlite(connection).Options);
        }

        public static async Task<Db> CreateAsync(params (int Id, string Url)[] images)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new Db(connection);
            await db.Context.Database.EnsureCreatedAsync();
            db.Context.MedDatas.AddRange(images.Select(image => new MedData { Id = image.Id, ImageUrl = image.Url }));
            await db.Context.SaveChangesAsync();
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
