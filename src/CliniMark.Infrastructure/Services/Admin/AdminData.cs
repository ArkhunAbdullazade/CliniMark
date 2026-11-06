using System.Globalization;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services.Admin;

/// <summary>Lookups shared by the admin pages and exports. Results link to people only by the copied email.</summary>
internal static class AdminData
{
    public const string SaveButton = "Save and Next Term";

    public static async Task<People> LoadPeopleAsync(MedDataDbContext context, CancellationToken cancellationToken = default)
    {
        var roles = await (from userRole in context.UserRoles
                           join role in context.Roles on userRole.RoleId equals role.Id
                           select new { userRole.UserId, role.Name }).AsNoTracking().ToListAsync(cancellationToken);
        var roleByUser = roles.GroupBy(item => item.UserId).ToDictionary(group => group.Key,
            group => group.Select(item => item.Name ?? "").OrderBy(name => name == IdentityService.AdminRole).First());
        var users = await context.Users.AsNoTracking().Select(user => new
        {
            user.Id, user.Email, user.FullName, user.Position, user.University, user.ClinicalExperience, user.OrcidId,
            user.Speciality, user.BodyRegion, user.ImageModality, user.CreatedAt
        }).ToListAsync(cancellationToken);

        // Codes follow signup order, so they stay stable between exports as new people join.
        var counters = new Dictionary<string, int>();
        var people = new List<Person>();
        foreach (var user in users.OrderBy(user => user.CreatedAt ?? DateTime.MinValue).ThenBy(user => user.Id, StringComparer.Ordinal))
        {
            var role = roleByUser.GetValueOrDefault(user.Id, "");
            if (role == IdentityService.AdminRole) continue;
            var prefix = role switch { "Professional" => "SPEC", "Medical_Student" => "STU", _ => "ACCT" };
            counters[prefix] = counters.GetValueOrDefault(prefix) + 1;
            people.Add(new Person(user.Id, user.Email, user.FullName, role, user.Position, user.University, user.ClinicalExperience,
                user.OrcidId, user.Speciality, user.BodyRegion, user.ImageModality, user.CreatedAt,
                $"{prefix}-{counters[prefix].ToString("000", CultureInfo.InvariantCulture)}"));
        }
        return new People(people);
    }

    /// <summary>(image, lower-case email) pairs where that specialist answered every term of the image.</summary>
    public static async Task<HashSet<(int ImageId, string Email)>> LoadCompleteSetsAsync(MedDataDbContext context, CancellationToken cancellationToken = default)
    {
        var answered = await context.AnnotatedMedDatas.AsNoTracking().Where(item => item.Email != null && item.ExtractedKeyword != null)
            .Select(item => new { item.MedDataId, item.Email, item.ExtractedKeyword }).Distinct().ToListAsync(cancellationToken);
        if (answered.Count == 0) return [];
        var keywords = await KeywordsForAnsweredImagesAsync(context, cancellationToken);
        return answered.GroupBy(item => (item.MedDataId, Email: item.Email!.ToLowerInvariant()))
            .Where(group => keywords.TryGetValue(group.Key.MedDataId, out var terms) && terms.Count > 0
                && terms.All(term => group.Any(item => item.ExtractedKeyword!.Trim() == term)))
            .Select(group => group.Key).ToHashSet();
    }

    public static async Task<Dictionary<int, HashSet<string>>> KeywordsForAnsweredImagesAsync(MedDataDbContext context, CancellationToken cancellationToken = default)
    {
        var rows = await context.MedDataKeywords.AsNoTracking()
            .Where(keyword => keyword.Keyword != null && keyword.Keyword.Trim() != ""
                && context.AnnotatedMedDatas.Any(answer => answer.MedDataId == keyword.MedDataId))
            .Select(keyword => new { keyword.MedDataId, keyword.Keyword }).ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.MedDataId).ToDictionary(group => group.Key,
            group => group.Select(row => row.Keyword!.Trim()).ToHashSet());
    }

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? "";
}

public static class ResponseTimings
{
    /// <summary>The browser stores "(ms until first draw, ms from first draw to save)"; "(0,0)" means nothing was drawn.</summary>
    public static (long? FirstDrawMs, long? DrawingMs) Parse(string? value)
    {
        var parts = value?.Trim().TrimStart('(').TrimEnd(')').Split(',');
        if (parts is not [var first, var drawing]
            || !long.TryParse(first.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstMs)
            || !long.TryParse(drawing.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var drawingMs)
            || firstMs < 0 || drawingMs < 0 || (firstMs == 0 && drawingMs == 0))
            return (null, null);
        return (firstMs, drawingMs);
    }
}

internal sealed class People
{
    private readonly Dictionary<string, Person> byId;
    private readonly Dictionary<string, Person> byEmail;

    public People(IReadOnlyList<Person> all)
    {
        All = all;
        byId = all.ToDictionary(person => person.Id);
        byEmail = all.Where(person => !string.IsNullOrWhiteSpace(person.Email))
            .GroupBy(person => person.Email!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Person> All { get; }

    public Person? ById(string? id) => id != null && byId.TryGetValue(id, out var person) ? person : null;

    public Person? ByEmail(string? email) => email != null && byEmail.TryGetValue(email, out var person) ? person : null;

    public PersonRef? Ref(string? id) => id == null ? null
        : ById(id) is { } person ? new PersonRef(person.Id, person.DisplayName, person.Role) : new PersonRef(id, "Removed account", "");

    public string NameForEmail(string? email) => ByEmail(email)?.DisplayName ?? "Removed account";
}
