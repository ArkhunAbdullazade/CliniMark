using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services.Admin;

/// <summary>
/// The results archive plus a copy of every study image, written straight to the response as each image arrives.
/// It can reach gigabytes, so it never collects on the container's small disk, and bytes keep flowing through the ingress.
/// </summary>
public sealed class ImageArchiveService(MedDataDbContext context, ResultsExportService results, HttpClient http)
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>Everything that can fail cleanly, before the first byte goes out. Dispose the result to allow the next one.</summary>
    public async Task<ImageArchive> PrepareAsync(ExportOptions options, CancellationToken cancellationToken = default)
    {
        if (!await OneAtATime.WaitAsync(0, cancellationToken))
            throw new ExportBusyException("An archive with all images is already downloading. Wait for it to finish, then try again.");
        Stream? data = null;
        try
        {
            data = await results.CreateArchiveAsync(options, cancellationToken);
            var images = await context.MedDatas.AsNoTracking().OrderBy(image => image.Id)
                .Select(image => new ImageArchive.Source(image.Id, image.ImageUrl, image.SourceUrl)).ToListAsync(cancellationToken);
            return new ImageArchive(data, images, options.Format, http, OneAtATime);
        }
        catch
        {
            if (data != null) await data.DisposeAsync();
            OneAtATime.Release();
            throw;
        }
    }
}

public sealed class ImageArchive : IAsyncDisposable
{
    internal sealed record Source(int Id, string? ImageUrl, string? SourceUrl);

    private const int Attempts = 3;
    // Three attempts stay well inside the 4 minutes the ingress lets a response sit idle.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly Stream data;
    private readonly IReadOnlyList<Source> images;
    private readonly ExportFormat format;
    private readonly HttpClient http;
    private readonly SemaphoreSlim slot;

    internal ImageArchive(Stream data, IReadOnlyList<Source> images, ExportFormat format, HttpClient http, SemaphoreSlim slot)
    {
        this.data = data;
        this.images = images;
        this.format = format;
        this.http = http;
        this.slot = slot;
    }

    public async Task WriteToAsync(Stream output, CancellationToken cancellationToken = default)
    {
        var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await CopyResultsAsync(zip, cancellationToken);

        var manifest = new List<object?[]>();
        var missing = new StringBuilder();
        await using var spool = new FileStream(Path.Combine(Path.GetTempPath(), $"clinimark-image-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        foreach (var image in images)
        {
            // Spooled first, because a part of an image already in the ZIP could not be taken back for a retry.
            var (extension, status) = await DownloadAsync(image.ImageUrl, spool, cancellationToken);
            if (extension == null)
            {
                manifest.Add([image.Id, null, null, null, status, image.ImageUrl, image.SourceUrl]);
                missing.Append($"{image.Id}\t{status}\t{image.ImageUrl}\r\n");
                continue;
            }
            spool.Position = 0;
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(spool, cancellationToken));
            spool.Position = 0;
            var file = $"images/{image.Id}.{extension}";
            // Images are already compressed, and recompressing thousands would only tie up the CPU annotators share.
            var entry = zip.CreateEntry(file, CompressionLevel.NoCompression);
            await using (var target = await entry.OpenAsync(cancellationToken))
                await spool.CopyToAsync(target, cancellationToken);
            manifest.Add([image.Id, file, spool.Length, hash, status, image.ImageUrl, image.SourceUrl]);
        }

        await WriteManifestAsync(zip, manifest, cancellationToken);
        if (missing.Length > 0)
            await WriteEntryAsync(zip, "MISSING_IMAGES.txt", Encoding.UTF8.GetBytes("ImageId\tProblem\tImageUrl\r\n" + missing), cancellationToken);

        // Only here: disposing writes the ZIP's directory, which would make a failed download look like a complete archive.
        await zip.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await data.DisposeAsync();
        slot.Release();
    }

    private async Task CopyResultsAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        var results = await ZipArchive.CreateAsync(data, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await using (results)
        {
            foreach (var source in results.Entries)
            {
                await using var reader = await source.OpenAsync(cancellationToken);
                await using var writer = await zip.CreateEntry(source.FullName, CompressionLevel.Optimal).OpenAsync(cancellationToken);
                await reader.CopyToAsync(writer, cancellationToken);
            }
        }
    }

    private async Task WriteManifestAsync(ZipArchive zip, List<object?[]> rows, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        using (var table = new ResultsExportService.TableWriter(
            new StreamWriter(memory, new UTF8Encoding(format == ExportFormat.Csv), 1 << 16, leaveOpen: true), format, ownsWriter: true))
        {
            table.Header("ImageId", "File", "Bytes", "Sha256", "Status", "ImageUrl", "SourceUrl");
            foreach (var row in rows) table.Row(row);
        }
        await WriteEntryAsync(zip, "images_manifest" + (format == ExportFormat.Csv ? ".csv" : ".json"), memory.ToArray(), cancellationToken);
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] content, CancellationToken cancellationToken)
    {
        await using var target = await zip.CreateEntry(name, CompressionLevel.Optimal).OpenAsync(cancellationToken);
        await target.WriteAsync(content, cancellationToken);
    }

    /// <summary>Leaves the image in the spool. Returns its file extension, or none and the reason it could not be fetched.</summary>
    private async Task<(string? Extension, string Status)> DownloadAsync(string? url, FileStream spool, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
            return (null, "invalid-link");
        var status = "";
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            if (attempt > 1) await Task.Delay(RetryDelay, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AttemptTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                // Publisher sites refuse clients that don't look like a browser; local runs still read from them.
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/png,image/*,*/*;q=0.8");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    status = "http-" + (int)response.StatusCode;
                    if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500) return (null, status);
                    continue;
                }
                spool.SetLength(0);
                await response.Content.CopyToAsync(spool, timeout.Token);
                var extension = await ImageExtensionAsync(spool, timeout.Token);
                return extension == null ? (null, "not-an-image") : (extension, "ok");
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException
                || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                status = exception is OperationCanceledException ? "timeout" : "network-error";
            }
        }
        return (null, status);
    }

    private static async Task<string?> ImageExtensionAsync(FileStream spool, CancellationToken cancellationToken)
    {
        var head = new byte[12];
        spool.Position = 0;
        var length = await spool.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var bytes = head.AsSpan(0, length);
        if (bytes.StartsWith<byte>([0xFF, 0xD8, 0xFF])) return "jpg";
        if (bytes.StartsWith<byte>([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A])) return "png";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "gif";
        if (length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "webp";
        if (length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8) && (bytes[8..12].SequenceEqual("avif"u8) || bytes[8..12].SequenceEqual("avis"u8))) return "avif";
        if (bytes.StartsWith("BM"u8)) return "bmp";
        return null;
    }
}
