using CliniMark.Core.Services;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services;

/// <summary>
/// Fetches a study image from Azure blob storage for /study-images/{id}/{file} (see StudyImageAddress). It only ever
/// requests the exact address the database holds for that image, and only when the file name in the request matches
/// it, so the route can't be used to fetch anything else. The image is streamed, never held in memory.
/// </summary>
public sealed class StudyImageProxy(MedDataDbContext context, HttpClient http)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public enum Outcome { Found, NotFound, Unavailable }

    /// <summary>The image's bytes and type. Disposing it closes the connection to storage.</summary>
    public sealed class Image(HttpResponseMessage response, Stream content, string contentType, long? length) : IAsyncDisposable
    {
        public Stream Content { get; } = content;
        public string ContentType { get; } = contentType;
        public long? Length { get; } = length;

        public async ValueTask DisposeAsync()
        {
            await Content.DisposeAsync();
            response.Dispose();
        }
    }

    public async Task<(Outcome Outcome, Image? Image)> OpenAsync(int imageId, string file, CancellationToken cancellationToken)
    {
        var stored = await context.MedDatas.AsNoTracking().Where(image => image.Id == imageId)
            .Select(image => image.ImageUrl).FirstOrDefaultAsync(cancellationToken);
        if (!StudyImageAddress.TryStorageFile(stored, out var storedFile) || storedFile != file)
            return (Outcome.NotFound, null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        HttpResponseMessage? response = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, stored!.Trim());
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response.Dispose();
                return (Outcome.NotFound, null);
            }
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || contentType == null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                response.Dispose();
                return (Outcome.Unavailable, null);
            }
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return (Outcome.Found, new Image(response, content, contentType, response.Content.Headers.ContentLength));
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            response?.Dispose();
            return (Outcome.Unavailable, null);
        }
    }
}
