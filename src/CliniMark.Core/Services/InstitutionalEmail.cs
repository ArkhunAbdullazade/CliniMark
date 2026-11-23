namespace CliniMark.Core.Services;

/// <summary>
/// Sign-up asks for an institutional or workplace email address. Addresses at personal email services (Gmail, Outlook,
/// Yahoo, iCloud and the like, including common European, Ukrainian, Russian and Azerbaijani ones) are refused. A list
/// can never be complete, so the study team still checks new registrations; accounts the study team creates are not
/// checked.
/// </summary>
public static class InstitutionalEmail
{
    public const string Required = "Use your institutional or workplace email address. Personal email services such as Gmail, Outlook, Hotmail, Yahoo or iCloud aren't accepted.";

    // Personal services that exist under many country endings: gmail.com, hotmail.co.uk, yahoo.com.br, gmx.de, ...
    private static readonly HashSet<string> Families = new(StringComparer.Ordinal)
    {
        "gmail", "googlemail", "hotmail", "outlook", "live", "msn", "windowslive", "yahoo", "ymail", "aol", "icloud",
        "gmx", "yandex", "protonmail", "proton", "zoho", "zohomail", "tutanota", "tuta"
    };

    // Second-level labels of country endings such as .co.uk or .com.br.
    private static readonly HashSet<string> CountrySecondLevels = new(StringComparer.Ordinal) { "co", "com", "net", "org" };

    private static readonly HashSet<string> Domains = new(StringComparer.Ordinal)
    {
        // International
        "rocketmail.com", "aim.com", "me.com", "mac.com", "pm.me", "mail.com", "email.com", "usa.com", "inbox.com",
        "fastmail.com", "fastmail.fm", "hey.com", "hushmail.com", "mailfence.com", "gmx.us", "ya.ru", "passport.com",
        // Germany, Austria, Switzerland
        "web.de", "t-online.de", "freenet.de", "posteo.de", "posteo.net", "mailbox.org", "arcor.de", "online.de", "email.de",
        "bluewin.ch",
        // Ukraine, Russia and neighbours
        "ukr.net", "i.ua", "meta.ua", "bigmir.net", "mail.ru", "bk.ru", "list.ru", "inbox.ru", "internet.ru", "rambler.ru",
        "mail.az", "box.az", "mail.ua",
        // Elsewhere in Europe
        "libero.it", "virgilio.it", "tiscali.it", "alice.it", "orange.fr", "wanadoo.fr", "laposte.net", "free.fr", "sfr.fr",
        "seznam.cz", "centrum.cz", "wp.pl", "o2.pl", "interia.pl", "onet.pl", "abv.bg", "btinternet.com", "sky.com",
        "virginmedia.com",
        // North America and Asia
        "comcast.net", "att.net", "verizon.net", "sbcglobal.net", "qq.com", "163.com", "126.com", "yeah.net", "sina.com",
        "sohu.com", "naver.com", "daum.net", "hanmail.net", "rediffmail.com"
    };

    // For the sign-up page's own check, which follows the same rules before the form is sent (signupConfirm.js).
    public static IEnumerable<string> PersonalDomains => Domains.Order(StringComparer.Ordinal);
    public static IEnumerable<string> PersonalFamilies => Families.Order(StringComparer.Ordinal);

    /// <summary>True when the address is at a known personal email service.</summary>
    public static bool IsPersonal(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at < 0) return false;
        var domain = email![(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        if (Domains.Contains(domain)) return true;
        var labels = domain.Split('.');
        return labels.Length switch
        {
            2 => Families.Contains(labels[0]),
            3 => Families.Contains(labels[0]) && CountrySecondLevels.Contains(labels[1]) && labels[2].Length == 2,
            _ => false
        };
    }
}
