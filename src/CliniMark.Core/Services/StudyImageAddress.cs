using System.Text.RegularExpressions;

namespace CliniMark.Core.Services;

/// <summary>
/// Pages show study images held in Azure blob storage through the app's own address, /study-images/{image id}/{file},
/// so a network that lets CliniMark through also lets its images through: some hospital web filters block Azure blob
/// storage, and the images then failed only on those computers. The database keeps the storage address; exports and
/// the image archive still use it. Any other address (local samples, other hosts) is shown as it is.
/// </summary>
public static partial class StudyImageAddress
{
    public const string RoutePrefix = "/study-images";

    /// <summary>The address a page should use for this image.</summary>
    public static string? For(int imageId, string? storedUrl) =>
        TryStorageFile(storedUrl, out var file) ? $"{RoutePrefix}/{imageId}/{file}" : storedUrl;

    /// <summary>
    /// True for an https address of a file in Azure blob storage, without a query or fragment; file is its last path
    /// segment (the stored images are named by the SHA-256 of their bytes, so a file name never changes content).
    /// </summary>
    public static bool TryStorageFile(string? storedUrl, out string file)
    {
        file = "";
        if (!Uri.TryCreate(storedUrl?.Trim(), UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps
            || !address.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
            || address.Query.Length > 0 || address.Fragment.Length > 0 || !address.IsDefaultPort)
            return false;
        var last = address.Segments[^1];
        if (!SafeFile().IsMatch(last)) return false;
        file = last;
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$")]
    private static partial Regex SafeFile();
}
