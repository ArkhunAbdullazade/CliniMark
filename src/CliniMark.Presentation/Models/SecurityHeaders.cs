namespace CliniMark.Presentation.Models;

public static class SecurityHeaders
{
    public const string NonceKey = "csp-nonce";

    // Study images in blob storage load through the app (/study-images, StudyImageAddress); img-src stays open for any other
    // image address (workbook links locally) and for older pages still open in a browser. Razor already encodes page text;
    // this limits the damage if anything ever slipped through.
    public static string Policy(string nonce) =>
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src * data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
}
