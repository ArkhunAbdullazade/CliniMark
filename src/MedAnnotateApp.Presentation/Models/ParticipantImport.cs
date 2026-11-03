using System.Text;
using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Presentation.Dtos;

namespace MedAnnotateApp.Presentation.Models;

/// <summary>
/// Operator command <c>create-participants &lt;participants.csv&gt; &lt;credentials.csv&gt;</c>: the study team creates the
/// participants' accounts instead of public signup. The whole file is checked first and nothing is created if any row
/// is invalid. Existing accounts are skipped and never changed. Each new account gets a generated password, written
/// only to the credentials file (which must not exist yet), never to the console.
///
/// Input columns (header row, any order): Email, FullName, Position, University, ClinicalExperience, OrcidId,
/// Speciality, BodyRegion, ImageModality. Position is one of the signup positions; "medical student" gets the student
/// workspace, every other position the specialist workspace. The three filter columns hold labels from the app's lists
/// separated by ";" and may be empty: the participant then chooses on first sign-in.
/// </summary>
public static class ParticipantImport
{
    public static readonly string[] Columns =
        ["Email", "FullName", "Position", "University", "ClinicalExperience", "OrcidId", "Speciality", "BodyRegion", "ImageModality"];
    public static readonly string[] Positions = ["specialist", "general practitioner", "resident/assistant", "medical student"];

    public sealed record Participant(int Line, User User, string Role);

    public static (List<Participant> Participants, List<string> Errors) Parse(string csvText)
    {
        var participants = new List<Participant>();
        var errors = new List<string>();
        var rows = Csv.Parse(csvText);
        if (rows.Count == 0)
        {
            errors.Add("The file is empty.");
            return (participants, errors);
        }

        var header = rows[0].Select(name => name.Trim()).ToList();
        var missing = Columns.Where(column => !header.Contains(column, StringComparer.OrdinalIgnoreCase)).ToList();
        var unknown = header.Where(name => !Columns.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) errors.Add($"Missing columns: {string.Join(", ", missing)}.");
        if (unknown.Count > 0) errors.Add($"Unknown columns: {string.Join(", ", unknown)} (a misspelt column would be ignored).");
        if (errors.Count > 0) return (participants, errors);

        var index = Columns.ToDictionary(column => column, column => header.FindIndex(name => name.Equals(column, StringComparison.OrdinalIgnoreCase)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var line = r + 1;
            string Get(string column) => index[column] < row.Count ? row[index[column]].Trim() : string.Empty;
            var rowErrors = new List<string>();

            var email = Get("Email");
            if (!EmailFormat.IsValid(email)) rowErrors.Add($"'{email}' is not a complete email address");
            else if (!seen.Add(email)) rowErrors.Add($"{email} appears more than once");
            var fullName = Get("FullName");
            if (fullName.Length is 0 or > 200) rowErrors.Add("FullName is required (at most 200 characters)");
            var position = Get("Position").ToLowerInvariant();
            if (!Positions.Contains(position)) rowErrors.Add($"Position must be one of: {string.Join(", ", Positions)}");
            var university = Get("University");
            if (university.Length is 0 or > 300) rowErrors.Add("University is required (at most 300 characters)");
            var experienceText = Get("ClinicalExperience");
            var experience = 0;
            if (experienceText.Length > 0 && (!int.TryParse(experienceText, out experience) || experience is < 0 or > 80))
                rowErrors.Add("ClinicalExperience must be a whole number of years from 0 to 80");
            var orcid = Get("OrcidId");
            if (orcid.Length > 100) rowErrors.Add("OrcidId must be at most 100 characters");

            string Filter(string column, string[] allowed)
            {
                var values = Get(column).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var value in values.Where(value => !allowed.Contains(value, StringComparer.OrdinalIgnoreCase)))
                    rowErrors.Add($"{column} '{value}' is not in the app's list");
                return ImageFiltersDto.JoinSelection(values);
            }
            var speciality = Filter("Speciality", ImageFilterOptions.Specialities);
            var bodyRegion = Filter("BodyRegion", ImageFilterOptions.BodyRegions);
            var modality = Filter("ImageModality", ImageFilterOptions.ImageModalities);

            if (rowErrors.Count > 0)
            {
                errors.Add($"Line {line}: {string.Join("; ", rowErrors)}.");
                continue;
            }

            var user = new User
            {
                Email = email, UserName = email, FullName = fullName, Position = position, University = university,
                ClinicalExperience = experience, OrcidId = orcid.Length > 0 ? orcid : null,
                Speciality = speciality, BodyRegion = bodyRegion, ImageModality = modality,
            };
            participants.Add(new Participant(line, user, position == "medical student" ? "Medical_Student" : "Professional"));
        }

        if (participants.Count == 0 && errors.Count == 0) errors.Add("The file has no participant rows.");
        return (participants, errors);
    }

    public static async Task<int> RunAsync(string inputPath, string credentialsPath, IIdentityService identityService, TextWriter output)
    {
        if (!File.Exists(inputPath))
        {
            await output.WriteLineAsync($"Not found: {inputPath}");
            return 2;
        }
        if (File.Exists(credentialsPath))
        {
            await output.WriteLineAsync($"{credentialsPath} already exists. Choose a new file name; existing passwords are never overwritten.");
            return 2;
        }

        var (participants, errors) = Parse(await File.ReadAllTextAsync(inputPath));
        if (errors.Count > 0)
        {
            await output.WriteLineAsync("Nothing was created. Fix these rows and run again:");
            foreach (var error in errors) await output.WriteLineAsync("  " + error);
            return 1;
        }

        var credentials = new StringBuilder("Email,FullName,Role,Password,Status\r\n");
        int created = 0, skipped = 0;
        foreach (var participant in participants)
        {
            var (succeeded, password, failures) = await identityService.CreateParticipantAsync(participant.User, participant.Role);
            var status = succeeded ? "created" : "not created: " + string.Join(" ", failures ?? []);
            if (succeeded) created++; else skipped++;
            credentials.Append(string.Join(",", new[] { participant.User.Email, participant.User.FullName, participant.Role, password ?? "", status }
                .Select(Csv.Quote))).Append("\r\n");
            await output.WriteLineAsync($"Line {participant.Line}: {participant.User.Email} ({participant.Role}) {status}");
        }

        await File.WriteAllTextAsync(credentialsPath, credentials.ToString(), new UTF8Encoding(true));
        await output.WriteLineAsync($"{created} created, {skipped} not created. Passwords are only in {credentialsPath}: share each one privately, then delete the file.");
        return skipped == 0 ? 0 : 3;
    }

    /// <summary>Minimal RFC 4180 reader/writer: quoted fields, doubled quotes, commas and line breaks inside quotes.</summary>
    public static class Csv
    {
        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            text = text.TrimStart('﻿');
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c is '\r' or '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = [];
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        public static string Quote(string? value)
        {
            value ??= string.Empty;
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
