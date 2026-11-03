using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Presentation.Dtos;
using MedAnnotateApp.Presentation.Models;
using Xunit;

namespace MedAnnotateApp.Tests;

public class DisplayLabelsAndParticipantsTests
{
    // The researcher's labels (MedAnnotateDatasets/app_labels/label_vocabulary.csv, app-labels-1.1.0, scope "annotation").
    private static readonly string[] VocabularyBodyRegions =
    [
        "Chest and Thorax", "Abdomen", "Brain", "Head and Neck", "Unknown", "Pelvis", "Lower extremity", "Spine", "Vascular",
        "Upper Extremity", "Skin and Soft tissue", "Eye orbit", "Breast", "Whole Body"
    ];
    private static readonly string[] VocabularySpecialities =
    [
        "Gastroenterology/Hepatology", "Neurology/Neurosurgery", "Pulmonology", "Oncology", "Orthopedics", "Surgery", "Cardiology",
        "Vascular diseases", "Urology/Nephrology", "Dentistry", "Infectious diseases", "Obstetrics/Gynecology", "Pediatrics",
        "Emergency/Critical Care", "Endocrinology", "Dermatology", "Ophthalmology", "Otorhinolaryngology", "Hematology",
        "Immunology/Allergies", "Unknown"
    ];
    private static readonly string[] VocabularyModalities = ["CT", "X-ray", "MRI", "Microscopy", "Ultrasound", "Endoscopy", "Angiography", "Fundoscopy"];

    [Fact]
    public void FilterListsAreExactlyTheDatasetDisplayLabels()
    {
        Assert.Equal(VocabularyBodyRegions.Order(), ImageFilterOptions.BodyRegions.Order());
        Assert.Equal(VocabularySpecialities.Order(), ImageFilterOptions.Specialities.Order());
        Assert.Equal(VocabularyModalities.Order(), ImageFilterOptions.ImageModalities.Order());
        Assert.DoesNotContain("Clinical photographs", ImageFilterOptions.ImageModalities); // NEJM only, not in the app yet
        Assert.Equal("Unknown", ImageFilterOptions.BodyRegions[^1]);
        Assert.Equal("Unknown", ImageFilterOptions.Specialities[^1]);
    }

    [Fact]
    public void LabelsSurviveStorageBecauseNoneHoldsTheSelectionSeparator()
    {
        foreach (var label in ImageFilterOptions.BodyRegions.Concat(ImageFilterOptions.Specialities).Concat(ImageFilterOptions.ImageModalities))
        {
            Assert.DoesNotContain(',', label);
            Assert.Equal(label.Trim(), label);
        }
    }

    [Fact]
    public void EveryLabelSelectsExactlyTheImagesThatCarryIt()
    {
        foreach (var region in ImageFilterOptions.BodyRegions)
        {
            var stored = ImageFiltersDto.JoinSelection([region]);
            var filters = ImageSelectionFilters.Parse(null, stored, null);
            foreach (var other in ImageFilterOptions.BodyRegions)
                Assert.Equal(other == region, filters.Matches(new MedData { BodyRegion = other, Speciality = "Surgery", Modality = "CT" }));
        }
        var mixed = ImageSelectionFilters.Parse(ImageFiltersDto.JoinSelection(["Emergency/Critical Care", "Unknown"]), null,
            ImageFiltersDto.JoinSelection(["X-ray", "MRI"]));
        Assert.True(mixed.Matches(new MedData { Speciality = "Emergency/Critical Care", BodyRegion = "Spine", Modality = "X-ray" }));
        Assert.True(mixed.Matches(new MedData { Speciality = " unknown ", BodyRegion = "Spine", Modality = "mri" }));
        Assert.False(mixed.Matches(new MedData { Speciality = "Surgery", BodyRegion = "Spine", Modality = "X-ray" }));
        Assert.False(mixed.Matches(new MedData { Speciality = "Unknown", BodyRegion = "Spine", Modality = "CT" }));
    }

    [Theory]
    [InlineData("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/")]
    [InlineData("CC BY-NC 4.0", "https://creativecommons.org/licenses/by-nc/4.0/")]
    [InlineData("CC BY-NC-SA 4.0", "https://creativecommons.org/licenses/by-nc-sa/4.0/")]
    [InlineData(" CC BY-SA 3.0 ", "https://creativecommons.org/licenses/by-sa/3.0/")]
    [InlineData("CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/")]
    [InlineData("All rights reserved", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void LicenceNamesLinkToTheirCreativeCommonsDeed(string? licence, string? expected) =>
        Assert.Equal(expected, LicenceLinks.UrlFor(licence));

    private const string Header = "Email,FullName,Position,University,ClinicalExperience,OrcidId,Speciality,BodyRegion,ImageModality";

    [Fact]
    public void ParticipantFileBecomesAccountsWithRolesAndNormalizedFilters()
    {
        var (participants, errors) = ParticipantImport.Parse(
            "﻿" + Header + "\r\n" +
            "a.radiologist@hospital.example,\"Doe, Ann\",Specialist,Uni Hospital,12,0000-0001,Pulmonology; Oncology,Chest and Thorax,ct;x-ray\r\n" +
            "\r\n" +
            "b.student@uni.example,Ben Student,medical student,Medical School,,,,,\n");

        Assert.Empty(errors);
        Assert.Equal(2, participants.Count);
        var first = participants[0];
        Assert.Equal(2, first.Line);
        Assert.Equal("Professional", first.Role);
        Assert.Equal("Doe, Ann", first.User.FullName);
        Assert.Equal("specialist", first.User.Position);
        Assert.Equal(12, first.User.ClinicalExperience);
        Assert.Equal("pulmonology,oncology", first.User.Speciality);
        Assert.Equal("chest and thorax", first.User.BodyRegion);
        Assert.Equal("ct,x-ray", first.User.ImageModality);
        Assert.Equal("a.radiologist@hospital.example", first.User.UserName);
        var student = participants[1];
        Assert.Equal(4, student.Line);
        Assert.Equal("Medical_Student", student.Role);
        Assert.Equal(string.Empty, student.User.BodyRegion); // chooses filters on first sign-in
        Assert.Null(student.User.OrcidId);
    }

    [Fact]
    public void InvalidParticipantRowsAreAllReportedWithTheirLine()
    {
        var (participants, errors) = ParticipantImport.Parse(Header + "\n" +
            "not-an-email,Name,specialist,Uni,1,,,,\n" +
            "dup@x.example,Name,surgeon,Uni,1,,,,\n" +
            "dup@x.example,Name,specialist,,99,,Astrology,Thorax,photo\n");

        Assert.Empty(participants);
        Assert.Equal(3, errors.Count);
        Assert.StartsWith("Line 2:", errors[0]);
        Assert.Contains("not a complete email address", errors[0]);
        Assert.Contains("Position must be one of", errors[1]);
        Assert.Contains("appears more than once", errors[2]);
        Assert.Contains("University is required", errors[2]);
        Assert.Contains("ClinicalExperience", errors[2]);
        Assert.Contains("Speciality 'Astrology'", errors[2]);
        Assert.Contains("BodyRegion 'Thorax'", errors[2]);
        Assert.Contains("ImageModality 'photo'", errors[2]);
    }

    [Fact]
    public void ParticipantFileNeedsEveryColumnAndNoUnknownOnes()
    {
        Assert.Contains("Missing columns: OrcidId.", ParticipantImport.Parse(Header.Replace(",OrcidId", "") + "\n").Errors);
        Assert.Contains(ParticipantImport.Parse(Header + ",Hospital\n").Errors, error => error.StartsWith("Unknown columns: Hospital"));
        Assert.Contains("The file has no participant rows.", ParticipantImport.Parse(Header + "\n").Errors);
        Assert.Contains("The file is empty.", ParticipantImport.Parse("").Errors);
    }

    [Fact]
    public async Task CommandCreatesNothingWhenAnyRowIsInvalid()
    {
        using var folder = new TempFolder();
        var input = folder.Write("p.csv", Header + "\nok@x.example,Ok,specialist,Uni,1,,,,\nbad,Bad,specialist,Uni,1,,,,\n");
        var identity = new FakeIdentity();
        var output = new StringWriter();

        Assert.Equal(1, await ParticipantImport.RunAsync(input, folder.Path("credentials.csv"), identity, output));
        Assert.Empty(identity.Created);
        Assert.False(File.Exists(folder.Path("credentials.csv")));
        Assert.Contains("Nothing was created", output.ToString());
    }

    [Fact]
    public async Task CommandWritesPasswordsOnlyToANewCredentialsFile()
    {
        using var folder = new TempFolder();
        var input = folder.Write("p.csv", Header + "\nnew@x.example,New Person,resident/assistant,Uni,2,,Surgery,Abdomen,CT\n" +
                                          "existing@x.example,Existing,specialist,Uni,5,,,,MRI\n");
        var identity = new FakeIdentity { Existing = { "existing@x.example" } };
        var output = new StringWriter();
        var credentials = folder.Path("credentials.csv");

        Assert.Equal(3, await ParticipantImport.RunAsync(input, credentials, identity, output));
        var created = Assert.Single(identity.Created);
        Assert.Equal(("new@x.example", "Professional"), (created.User.Email, created.Role));
        var rows = ParticipantImport.Csv.Parse(await File.ReadAllTextAsync(credentials));
        Assert.Equal(["Email", "FullName", "Role", "Password", "Status"], rows[0]);
        Assert.Equal(["new@x.example", "New Person", "Professional", FakeIdentity.Password, "created"], rows[1]);
        Assert.Equal("existing@x.example", rows[2][0]);
        Assert.Equal("", rows[2][3]);
        Assert.StartsWith("not created", rows[2][4]);
        Assert.DoesNotContain(FakeIdentity.Password, output.ToString());

        // A second run never overwrites the passwords already handed out.
        var before = await File.ReadAllTextAsync(credentials);
        Assert.Equal(2, await ParticipantImport.RunAsync(input, credentials, identity, new StringWriter()));
        Assert.Equal(before, await File.ReadAllTextAsync(credentials));
    }

    [Fact]
    public void CsvRoundTripsQuotesCommasAndLineBreaks()
    {
        var values = new[] { "plain", "a,b", "say \"hi\"", "two\nlines", "" };
        var line = string.Join(",", values.Select(ParticipantImport.Csv.Quote));
        Assert.Equal(values, ParticipantImport.Csv.Parse(line + "\r\n")[0]);
    }

    private sealed class FakeIdentity : IIdentityService
    {
        public const string Password = "Generated-Password-7a";
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(User User, string Role)> Created { get; } = [];

        public Task<(bool Succeeded, string? TemporaryPassword, IEnumerable<string>? Errors)> CreateParticipantAsync(User user, string roleName)
        {
            if (Existing.Contains(user.Email!)) return Task.FromResult<(bool, string?, IEnumerable<string>?)>((false, null, ["already exists"]));
            Created.Add((user, roleName));
            return Task.FromResult<(bool, string?, IEnumerable<string>?)>((true, Password, null));
        }

        public Task<(bool Succeeded, IEnumerable<string>? Errors)> LoginAsync(string? email, string? password) => throw new NotSupportedException();
        public Task<(bool Succeeded, IEnumerable<string>? Errors)> SignupAsync(User user, string? password, string roleName) => throw new NotSupportedException();
        public Task SignoutAsync() => throw new NotSupportedException();
        public Task<(bool Succeeded, string? TemporaryPassword, IEnumerable<string>? Errors)> ResetPasswordAsync(string? email) => throw new NotSupportedException();
        public Task<(bool Succeeded, string? TemporaryPassword, IEnumerable<string>? Errors)> CreateAdminAsync(string? email) => throw new NotSupportedException();
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("medannotate-participants-").FullName;
        public string Path(string name) => System.IO.Path.Combine(root, name);
        public string Write(string name, string text)
        {
            File.WriteAllText(Path(name), text);
            return Path(name);
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
