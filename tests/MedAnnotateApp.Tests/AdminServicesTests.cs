using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Infrastructure.Data;
using MedAnnotateApp.Infrastructure.Repositories;
using MedAnnotateApp.Infrastructure.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MedAnnotateApp.Tests;

public sealed class AdminServicesTests
{
    private const string Coordinates = """
        {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"rectangle","points":[[10,20],[100,20],[100,80],[10,80]]}]}
        """;

    [Fact]
    public async Task ReleasingPartialWorkKeepsTheAnswersAndTheNextSpecialistStartsAFullSet()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);

        var result = await db.Assignments.ReleaseAsync(1, false, db.First.Id, db.Admin.Id);

        Assert.True(result.Released);
        Assert.Equal(1, result.KeptResponses);
        var image = await db.Context.MedDatas.AsNoTracking().SingleAsync(item => item.Id == 1);
        Assert.Null(image.LockedByUserId);
        Assert.Null(image.LockedAt);
        Assert.Null(image.KeywordStates);
        var audit = await db.Context.AdminActions.SingleAsync();
        Assert.Equal("release-specialist-lock", audit.Action);
        Assert.Equal(db.Admin.Id, audit.AdminUserId);
        Assert.Equal(db.First.Id, audit.TargetUserId);
        Assert.DoesNotContain("@", audit.Details);

        Assert.Equal(1, (await db.AssignAsync(db.Second)).MedData?.Id);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Second, Answer("lesion"))).Success);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Second, Answer("margin"))).Success);
        Assert.True((await db.Repository.SaveMetadataChecksAsync(db.Second, 1, LabelChecks.Answered)).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(1, db.Second)).Success);
        // The first specialist's next save is refused with a message that tells them to reload.
        var late = await db.Repository.SaveProfessionalAsync(db.First, Answer("margin"));
        Assert.False(late.Success);
        Assert.Contains("Reload the page", late.Message);

        var complete = await db.Queries.GetResponsesAsync(new ResponseQuery { Completeness = Completeness.CompleteOnly });
        var incomplete = await db.Queries.GetResponsesAsync(new ResponseQuery { Completeness = Completeness.IncompleteOnly });
        var all = await db.Queries.GetResponsesAsync(new ResponseQuery());
        Assert.Equal(2, complete.Total);
        Assert.All(complete.Items, row => Assert.Equal(db.Second.Id, row.AnnotatorId));
        Assert.Equal(db.First.Id, Assert.Single(incomplete.Items).AnnotatorId);
        Assert.Equal(3, all.Total);
    }

    [Fact]
    public async Task ReleaseIsRefusedWhenTheLockChangedHands()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);

        var result = await db.Assignments.ReleaseAsync(1, false, db.Second.Id, db.Admin.Id);

        Assert.False(result.Released);
        Assert.Equal(db.First.Id, (await db.Context.MedDatas.AsNoTracking().SingleAsync(item => item.Id == 1)).LockedByUserId);
        Assert.Empty(await db.Context.AdminActions.ToListAsync());
    }

    [Fact]
    public async Task SweeperReleasesOnlyIdleEmptyLocks()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);               // image 1, will hold partial work
        await db.AssignAsync(db.Second);              // image 2, empty
        await db.AssignAsync(db.Student, true);       // image 1, student
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);
        var now = DateTime.UtcNow;

        Assert.Equal(0, await db.Assignments.ReleaseIdleEmptyLocksAsync(TimeSpan.FromHours(12), now));

        foreach (var image in await db.Context.MedDatas.ToListAsync())
        {
            if (image.LockedAt != null) image.LockedAt = now.AddHours(-13);
            if (image.StudentLockedAt != null) image.StudentLockedAt = now.AddHours(-13);
        }
        foreach (var answer in await db.Context.AnnotatedMedDatas.ToListAsync())
            answer.CreatedAt = now.AddHours(-13);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        Assert.Equal(2, await db.Assignments.ReleaseIdleEmptyLocksAsync(TimeSpan.FromHours(12), now));
        var images = await db.Context.MedDatas.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(db.First.Id, images[0].LockedByUserId);
        Assert.Null(images[0].LockedByStudentUserId);
        Assert.Null(images[1].LockedByUserId);
        Assert.Equal(1, await db.Context.AnnotatedMedDatas.CountAsync());
        Assert.All(await db.Context.AdminActions.ToListAsync(), action => Assert.Null(action.AdminUserId));

        var locks = await db.Queries.GetLocksAsync();
        var partial = Assert.Single(locks);
        Assert.True(partial.HasPartialWork);
        Assert.Equal(LockState.Stale, partial.State);
        Assert.Equal((1, 2), (partial.TermsSaved, partial.TermsTotal));
    }

    [Fact]
    public async Task OverviewCountsProgressPeopleAndGaps()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, new AnnotatedMedData
        {
            MedDataId = 1, ExtractedKeyword = "margin", PressedButton = "Uncertain/Skip", Timestamps = "(0,0)"
        })).Success);
        Assert.True((await db.Repository.SaveMetadataChecksAsync(db.First, 1, LabelChecks.Answered)).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(1, db.First)).Success);
        await db.AssignAsync(db.Student, true);

        var overview = await db.Queries.GetOverviewAsync();

        Assert.Equal(3, overview.TotalImages);
        Assert.Equal(new RoleProgress(2, 1, 0, 1), overview.Specialist);
        Assert.Equal(new RoleProgress(3, 0, 1, 2), overview.Student);
        Assert.Equal((2, 1), (overview.Specialists, overview.Students));
        Assert.Equal(2, overview.SpecialistResponses);
        Assert.Equal(2, overview.ActiveNow);
        Assert.Contains(overview.ResponseMix, bin => bin.Label == "Uncertain/Skip" && bin.Count == 1);
        Assert.Equal(1, overview.FirstDrawSeconds.Sum(bin => bin.Count));
        // Image 3 is dermatology: no specialist or student selected it.
        Assert.Equal(1, overview.UnreachableStudentImages);
        Assert.Equal(1, overview.ImagesMissingKeywords);
        Assert.Contains(overview.Categories["Speciality"], item => item.Value == "cardiology" && item.Images == 2);
        Assert.Equal(2, overview.Activity.Sum(bucket => bucket.Specialist));
    }

    [Fact]
    public async Task ParticipantsShowCompletedPartialAndRemainingWork()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);

        var participants = await db.Queries.GetParticipantsAsync();
        var first = participants.Single(row => row.Person.Id == db.First.Id);
        var detail = await db.Queries.GetParticipantAsync(db.First.Id);

        Assert.Equal(3, participants.Count);
        Assert.DoesNotContain(participants, row => row.Person.Id == db.Admin.Id);
        Assert.Equal((0, 1, 1), (first.ImagesCompleted, first.PartialImages, first.Responses));
        Assert.Equal([1], first.LockedImages);
        Assert.Equal(2, first.OpenImagesLeft);
        Assert.Equal("SPEC-001", first.Person.Code);
        var work = Assert.Single(detail!.Work);
        Assert.Equal((1, 2, true), (work.Answered, work.Total, work.Locked));
    }

    [Fact]
    public async Task ImagesFilterByStatusAndSearch()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);

        var inProgress = await db.Queries.GetImagesAsync(new ImageQuery { SpecialistStatus = "in-progress" });
        var dermatology = await db.Queries.GetImagesAsync(new ImageQuery { Speciality = "dermatology" });
        var byTerm = await db.Queries.GetImagesAsync(new ImageQuery { Search = "margin" });

        Assert.Equal(db.First.Id, Assert.Single(inProgress.Items).SpecialistHolder?.Id);
        Assert.Equal(3, Assert.Single(dermatology.Items).Id);
        Assert.Equal(1, Assert.Single(byTerm.Items).Id);
        var detail = await db.Queries.GetImageAsync(1);
        Assert.Equal(["lesion", "margin"], detail!.Keywords);
        Assert.Equal(db.First.Id, detail.SpecialistLock?.UserId);
    }

    [Theory]
    [InlineData(IdentityMode.Pseudonymous)]
    [InlineData(IdentityMode.Full)]
    [InlineData(IdentityMode.None)]
    public async Task ArchiveHoldsEveryTableAndOnlyFullIdentityHasEmails(IdentityMode identity)
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion", "=SUM(A1)"))).Success);
        await db.AssignAsync(db.Student, true);
        Assert.True((await db.Repository.SaveStudentAsync(db.Student,
            [new AnnotatedByStudentsMedData { MedDataId = 1, Coordinates = Coordinates, TextualAnnotation = "lesion, \"left\"" }])).Success);

        var files = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Identity = identity }));

        var expected = new List<string> { "README.txt", "custom_annotations.csv", "images.csv", "metadata_checks.csv", "problem_reports.csv", "progress.csv", "specialist_responses.csv", "student_annotations.csv" };
        if (identity != IdentityMode.None) expected.Add("participants.csv");
        Assert.Equal(expected.Order(), files.Keys.Order());
        Assert.Equal(identity == IdentityMode.Full, files.Values.Any(text => text.Contains("first@example.test")));
        var specialist = files["specialist_responses.csv"].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, specialist.Length);
        Assert.Contains("'=SUM(A1)", specialist[1]);
        Assert.Contains("\"lesion, \"\"left\"\"\"", files["student_annotations.csv"]);
        Assert.Equal(identity != IdentityMode.None, specialist[0].Contains("AnnotatorCode"));
    }

    [Fact]
    public async Task IncompleteOnlyArchiveAndJsonFormat()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);

        var files = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions
        {
            Completeness = Completeness.IncompleteOnly, Format = ExportFormat.Json
        }));

        using var answers = JsonDocument.Parse(files["specialist_responses.json"]);
        var row = Assert.Single(answers.RootElement.EnumerateArray());
        Assert.False(row.GetProperty("SetComplete").GetBoolean());
        Assert.Equal("SPEC-001", row.GetProperty("AnnotatorCode").GetString());
        Assert.Equal(1200, row.GetProperty("MsUntilFirstDraw").GetInt64());
        using var groups = JsonDocument.Parse(files["student_annotations.json"]);
        Assert.Empty(groups.RootElement.EnumerateArray());
        using var images = JsonDocument.Parse(files["images.json"]);
        Assert.Equal(3, images.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ImageExportCarriesSourceLicenceAndAttribution()
    {
        await using var db = await AdminDatabase.CreateAsync();
        var image = db.Context.MedDatas.Find(1)!;
        image.SourceLicense = "CC BY-NC 4.0";
        image.Attribution = "A. Author et al. \"Case report\". Journal (2024). Licensed under CC BY-NC 4.0.";
        await db.Context.SaveChangesAsync();

        var files = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Format = ExportFormat.Json }));

        using var images = JsonDocument.Parse(files["images.json"]);
        var first = images.RootElement.EnumerateArray().First(row => row.GetProperty("ImageId").GetInt32() == 1);
        Assert.Equal("CC BY-NC 4.0", first.GetProperty("SourceLicense").GetString());
        Assert.Equal(image.Attribution, first.GetProperty("Attribution").GetString());
    }

    [Theory]
    [InlineData(ExportFormat.Csv, ".csv")]
    [InlineData(ExportFormat.Json, ".json")]
    public async Task ImageArchiveHoldsResultsAndTheExactBytesOfEveryImage(ExportFormat format, string extension)
    {
        await using var db = await AdminDatabase.CreateAsync();
        db.Context.MedDatas.Find(3)!.ExcludedAt = DateTime.UtcNow;
        await db.Context.SaveChangesAsync();
        var files = new Dictionary<int, byte[]> { [1] = Jpeg(1), [2] = Png(2), [3] = Jpeg(3) };

        var archive = await WriteImageArchiveAsync(db, new ImageServer(request => files[IdOf(request)]), new ExportOptions { Format = format, IncludeImages = true });

        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        Assert.Equal(["README.txt", "custom_annotations" + extension, "images" + extension, "images/1.jpg", "images/2.png", "images/3.jpg", "images_manifest" + extension,
            "metadata_checks" + extension, "participants" + extension, "problem_reports" + extension, "progress" + extension, "specialist_responses" + extension, "student_annotations" + extension],
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.Equal(files[1], EntryBytes(zip, "images/1.jpg"));
        Assert.Equal(files[2], EntryBytes(zip, "images/2.png"));
        Assert.Equal(files[3], EntryBytes(zip, "images/3.jpg"));
        var manifest = Encoding.UTF8.GetString(EntryBytes(zip, "images_manifest" + extension));
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(files[2])), manifest);
        Assert.Contains("images/", Encoding.UTF8.GetString(EntryBytes(zip, "README.txt")));
    }

    [Fact]
    public async Task ImageArchiveRetriesAndNamesEveryImageItCouldNotFetch()
    {
        await using var db = await AdminDatabase.CreateAsync();
        var calls = new Dictionary<int, int>();
        var server = new ImageServer(request =>
        {
            var id = IdOf(request);
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return id switch
            {
                1 when calls[id] == 1 => throw new HttpRequestException("connection reset"),
                1 => Jpeg(1),
                2 => HttpStatusCode.NotFound,
                _ => Encoding.UTF8.GetBytes("<html>sign in</html>")
            };
        });

        var archive = await WriteImageArchiveAsync(db, server, new ExportOptions { IncludeImages = true });

        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        Assert.Equal(2, calls[1]);
        Assert.Equal(1, calls[2]);
        Assert.Equal(Jpeg(1), EntryBytes(zip, "images/1.jpg"));
        Assert.Single(zip.Entries, entry => entry.FullName.StartsWith("images/"));
        var missing = Encoding.UTF8.GetString(EntryBytes(zip, "MISSING_IMAGES.txt"));
        Assert.Contains("2\thttp-404\thttps://images.example.test/2.png", missing);
        Assert.Contains("3\tnot-an-image\thttps://images.example.test/3.png", missing);
        var manifest = Encoding.UTF8.GetString(EntryBytes(zip, "images_manifest.csv"));
        Assert.Contains("2,,,,http-404,", manifest);
    }

    [Fact]
    public async Task OnlyOneImageArchiveRunsAtATime()
    {
        await using var db = await AdminDatabase.CreateAsync();
        var service = new ImageArchiveService(db.Context, db.Export, new HttpClient(new ImageServer(_ => Jpeg(1))));
        var first = await service.PrepareAsync(new ExportOptions { IncludeImages = true });
        await Assert.ThrowsAsync<ExportBusyException>(() => service.PrepareAsync(new ExportOptions { IncludeImages = true }));
        await first.DisposeAsync();
        await using var next = await service.PrepareAsync(new ExportOptions { IncludeImages = true });
    }

    [Fact]
    public async Task InterruptedImageArchiveNeverLooksComplete()
    {
        await using var db = await AdminDatabase.CreateAsync();
        using var cancel = new CancellationTokenSource();
        var server = new ImageServer(request =>
        {
            if (IdOf(request) == 2) cancel.Cancel();
            return Jpeg(1);
        });
        var service = new ImageArchiveService(db.Context, db.Export, new HttpClient(server));
        await using var archive = await service.PrepareAsync(new ExportOptions { IncludeImages = true });
        using var output = new MemoryStream();
        var stream = new ForwardOnlyStream(output);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.WriteToAsync(stream, cancel.Token));

        Assert.ThrowsAny<InvalidDataException>(() => new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read));
    }

    private static async Task<byte[]> WriteImageArchiveAsync(AdminDatabase db, ImageServer server, ExportOptions options)
    {
        var service = new ImageArchiveService(db.Context, db.Export, new HttpClient(server));
        await using var archive = await service.PrepareAsync(options);
        using var output = new MemoryStream();
        await archive.WriteToAsync(new ForwardOnlyStream(output));
        return output.ToArray();
    }

    private static int IdOf(HttpRequestMessage request) => int.Parse(Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath));

    private static byte[] Jpeg(byte seed) => [0xFF, 0xD8, 0xFF, 0xE0, seed, .. Enumerable.Repeat(seed, 2000)];

    private static byte[] Png(byte seed) => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, seed];

    private static byte[] EntryBytes(ZipArchive zip, string name)
    {
        using var source = zip.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class ImageServer(Func<HttpRequestMessage, object> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request) switch
            {
                HttpStatusCode status => new HttpResponseMessage(status),
                byte[] body => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) },
                var other => throw new InvalidOperationException(other.ToString())
            });
    }

    /// <summary>Like the real response body, it can only be written forwards.</summary>
    private sealed class ForwardOnlyStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    }

    [Fact]
    public async Task ResponsesCsvFollowsTheFilter()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("margin"))).Success);

        await using var stream = await db.Export.CreateResponsesCsvAsync(new ResponseQuery { Term = "marg" }, IdentityMode.Pseudonymous);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = (await reader.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains(",margin,", lines[1]);
    }

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tcell")]
    public async Task CsvNeutralizesFormulasInEveryTextCellButJsonKeepsThem(string dangerous)
    {
        await using var db = await AdminDatabase.CreateAsync();
        db.First.FullName = dangerous;
        db.First.University = dangerous;
        await db.Context.SaveChangesAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion", dangerous))).Success);

        var csv = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Identity = IdentityMode.Full }));
        var json = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Identity = IdentityMode.Full, Format = ExportFormat.Json }));

        Assert.Contains("'" + dangerous, csv["specialist_responses.csv"]);
        Assert.Contains("'" + dangerous, csv["participants.csv"]);
        Assert.DoesNotContain("," + dangerous, csv["participants.csv"]);
        using var answers = JsonDocument.Parse(json["specialist_responses.json"]);
        Assert.Equal(dangerous, answers.RootElement[0].GetProperty("Comment").GetString());
    }

    [Fact]
    public async Task OpenPageHeartbeatKeepsALongIdleEmptyLockFromTheSweeper()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.Second);
        var now = DateTime.UtcNow;
        var image = await db.Context.MedDatas.SingleAsync(item => item.LockedByUserId == db.Second.Id);
        image.LockedAt = now.AddHours(-13);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // The open page checks in: the lock time is refreshed, so the sweeper leaves it alone however long the clinician takes.
        Assert.True(await db.Repository.RefreshAssignmentAsync(image.Id, db.Second.Id, false));
        Assert.False(await db.Repository.RefreshAssignmentAsync(image.Id, db.First.Id, false));
        Assert.Equal(0, await db.Assignments.ReleaseIdleEmptyLocksAsync(TimeSpan.FromHours(12), now));
        var refreshed = await db.Context.MedDatas.AsNoTracking().SingleAsync(item => item.Id == image.Id);
        Assert.Equal(db.Second.Id, refreshed.LockedByUserId);
        Assert.True(refreshed.LockedAt > now.AddMinutes(-1));
    }

    [Fact]
    public async Task ProblemReportsAreListedResolvedWithAuditCountedAndExported()
    {
        await using var db = await AdminDatabase.CreateAsync();
        var problems = new ProblemAdminService(db.Context);
        await db.Repository.AddProblemReportAsync(new ProblemReport
            { CreatedAt = DateTime.UtcNow, UserId = db.First.Id, MedDataId = 1, Kind = ProblemReport.UserReport, Term = "lesion", Message = "Image is blurred" });
        await db.Repository.AddProblemReportAsync(new ProblemReport
            { CreatedAt = DateTime.UtcNow.AddDays(-2), UserId = db.Student.Id, MedDataId = 2, Kind = ProblemReport.ImageLoadError, Message = "The study image did not load." });

        var open = await db.Queries.GetProblemsAsync(false, 1);
        Assert.Equal(2, open.Total);
        Assert.Equal(ProblemReport.ImageLoadError, open.Items[0].Kind);
        var typed = Assert.Single(open.Items, item => item.Kind == ProblemReport.UserReport);
        Assert.Equal("Image is blurred", typed.Message);
        Assert.Equal("lesion", typed.Term);
        Assert.Equal(db.First.Id, typed.Reporter!.Id);
        Assert.Equal(new ProblemSummary(2, 1), await db.Queries.GetProblemSummaryAsync());

        var resolved = await problems.SetResolvedAsync(typed.Id, true, db.Admin.Id);
        Assert.True(resolved.Released);
        Assert.False((await problems.SetResolvedAsync(typed.Id, true, db.Admin.Id)).Released);
        Assert.Equal(1, (await db.Queries.GetProblemsAsync(false, 1)).Total);
        var all = await db.Queries.GetProblemsAsync(true, 1);
        Assert.Equal(2, all.Total);
        Assert.Equal(db.Admin.Email, all.Items.Single(item => item.Id == typed.Id).ResolvedBy);
        Assert.Equal("resolve-problem", (await db.Context.AdminActions.SingleAsync()).Action);

        var csv = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Identity = IdentityMode.None }));
        Assert.Contains("Image is blurred", csv["problem_reports.csv"]);
        Assert.Contains("image-load-error", csv["problem_reports.csv"]);
        Assert.DoesNotContain(db.First.Email!, csv["problem_reports.csv"]);
    }

    [Fact]
    public async Task LabelChecksReachTheExportTheImagePageAndTheSummary()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveMetadataChecksAsync(db.First, 1,
        [
            new MetadataAnswer(MetadataCheck.Modality, MetadataCheck.Correct, null, false),
            new MetadataAnswer(MetadataCheck.Speciality, MetadataCheck.Corrected, "Rheumatology", true),
            new MetadataAnswer(MetadataCheck.BodyRegion, MetadataCheck.CannotTell, null, false)
        ])).Success);

        var files = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Identity = IdentityMode.Pseudonymous }));
        var rows = files["metadata_checks.csv"].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, rows.Length);
        Assert.Contains("MetadataCheckId", rows[0]);
        Assert.Contains("Rheumatology", files["metadata_checks.csv"]);
        Assert.DoesNotContain(db.First.Email!, files["metadata_checks.csv"]);
        Assert.Contains("metadata_checks", files["README.txt"]);

        var detail = await db.Queries.GetImageAsync(1);
        Assert.Equal(3, detail!.MetadataChecks.Count);

        var summary = await db.Queries.GetMetadataCheckSummaryAsync();
        Assert.Equal(1, summary.Fields.Single(field => field.Field == MetadataCheck.Modality).Confirmed);
        Assert.Equal(1, summary.Fields.Single(field => field.Field == MetadataCheck.Speciality).Corrected);
        Assert.Equal(1, summary.Fields.Single(field => field.Field == MetadataCheck.BodyRegion).CannotTell);
        Assert.Equal(new AddedMetadataValue(MetadataCheck.Speciality, "Rheumatology", 1), Assert.Single(summary.AddedValues));
    }

    [Fact]
    public async Task ExportsKeepCoordinatesByteForByte()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);

        var csv = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions()));
        var json = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions { Format = ExportFormat.Json }));

        Assert.Contains("\"" + Coordinates.Replace("\"", "\"\"") + "\"", csv["specialist_responses.csv"]);
        using var answers = JsonDocument.Parse(json["specialist_responses.json"]);
        Assert.Equal(Coordinates, answers.RootElement[0].GetProperty("Coordinates").GetString());
    }

    [Fact]
    public async Task RemovedImageIsNeverAssignedAndKeepsItsAnswers()
    {
        await using var db = await AdminDatabase.CreateAsync();
        await db.AssignAsync(db.First);
        await db.AssignAsync(db.Student, true);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.First, Answer("lesion"))).Success);

        var removed = await db.Assignments.SetRemovedAsync(1, true, db.Admin.Id);

        Assert.True(removed.Released);
        Assert.False((await db.Assignments.SetRemovedAsync(1, true, db.Admin.Id)).Released);
        var image = await db.Context.MedDatas.AsNoTracking().SingleAsync(item => item.Id == 1);
        Assert.NotNull(image.ExcludedAt);
        Assert.Null(image.LockedByUserId);
        Assert.Null(image.LockedByStudentUserId);
        Assert.Equal(1, await db.Context.AnnotatedMedDatas.CountAsync());
        Assert.False(await db.Repository.RefreshAssignmentAsync(1, db.First.Id, false));
        var late = await db.Repository.SaveProfessionalAsync(db.First, Answer("margin"));
        Assert.False(late.Success);
        Assert.True(late.Conflict);

        // The lowest open image is skipped for everyone, and it no longer counts in progress.
        var (next, counter) = await db.AssignAsync(db.First);
        Assert.Equal(2, next?.Id);
        Assert.Equal("0/1", counter);
        Assert.Equal(2, (await db.AssignAsync(db.Student, true)).MedData?.Id);
        var overview = await db.Queries.GetOverviewAsync(true);
        Assert.Equal(2, overview.TotalImages);
        var row = Assert.Single((await db.Queries.GetImagesAsync(new ImageQuery { SpecialistStatus = "removed" })).Items);
        Assert.True(row.Removed);
        var audit = await db.Context.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal("remove-image", audit.Action);
        Assert.Equal(1, audit.MedDataId);

        var archive = await ReadArchiveAsync(await db.Export.CreateArchiveAsync(new ExportOptions()));
        Assert.Contains(",true", archive["images.csv"].Split("\r\n")[1]);

        Assert.True((await db.Assignments.SetRemovedAsync(1, false, db.Admin.Id)).Released);
        db.Context.ChangeTracker.Clear();
        // The first specialist's other assignment is empty, so the restored image with their partial work can come back.
        Assert.Null((await db.Context.MedDatas.AsNoTracking().SingleAsync(item => item.Id == 1)).ExcludedAt);
        Assert.Equal("restore-image", (await db.Context.AdminActions.AsNoTracking().OrderBy(item => item.Id).LastAsync()).Action);
    }

    [Theory]
    [InlineData("name@gmail")]
    [InlineData("name")]
    [InlineData("name@example..org")]
    [InlineData("na me@example.org")]
    [InlineData("")]
    public void EmailFormatRefusesIncompleteAddresses(string email) => Assert.False(EmailFormat.IsValid(email));

    [Theory]
    [InlineData("name@gmail.com")]
    [InlineData("first.last+tag@med.example.edu.tr")]
    public void EmailFormatAcceptsCompleteAddresses(string email) => Assert.True(EmailFormat.IsValid(email));

    [Theory]
    [InlineData("(1200,3400)", 1200L, 3400L)]
    [InlineData("( 5 , 6 )", 5L, 6L)]
    [InlineData("(0,0)", null, null)]
    [InlineData("garbage", null, null)]
    [InlineData(null, null, null)]
    public void TimingsParseTheBrowserFormat(string? value, long? first, long? drawing) =>
        Assert.Equal((first, drawing), ResponseTimings.Parse(value));

    private static AnnotatedMedData Answer(string keyword, string? comment = null) => new()
    {
        MedDataId = 1, ExtractedKeyword = keyword, PressedButton = "Save and Next Term", BoxCoordinates = Coordinates,
        Timestamps = "(1200,3400)", Comment = comment
    };

    private static async Task<Dictionary<string, string>> ReadArchiveAsync(Stream stream)
    {
        await using (stream)
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var files = new Dictionary<string, string>();
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                files[entry.FullName] = await reader.ReadToEndAsync();
            }
            return files;
        }
    }

    private sealed class AdminDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MedDataDbContext Context { get; }
        public MedDataRepository Repository { get; }
        public AdminQueryService Queries { get; }
        public AssignmentAdminService Assignments { get; }
        public ResultsExportService Export { get; }
        public User First { get; } = Person("first", "specialist", "cardiology");
        public User Second { get; } = Person("second", "specialist", "cardiology");
        public User Student { get; } = Person("student", "medical student", "cardiology");
        public User Admin { get; } = new() { Id = "admin", Email = "admin@example.test", UserName = "admin@example.test" };

        private AdminDatabase(SqliteConnection connection)
        {
            this.connection = connection;
            Context = new MedDataDbContext(new DbContextOptionsBuilder<MedDataDbContext>().UseSqlite(connection).Options);
            Repository = new MedDataRepository(Context);
            Queries = new AdminQueryService(Context, new MemoryCache(new MemoryCacheOptions()), Options.Create(new AdminOptions()));
            Assignments = new AssignmentAdminService(Context);
            Export = new ResultsExportService(Context);
        }

        public static async Task<AdminDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AdminDatabase(connection);
            await db.Context.Database.EnsureCreatedAsync();
            var roles = new[] { "Professional", "Medical_Student", "Admin" }
                .Select(name => new IdentityRole(name) { Id = name, NormalizedName = name.ToUpperInvariant() }).ToList();
            db.Context.Roles.AddRange(roles);
            var created = DateTime.UtcNow.AddDays(-1);
            foreach (var (user, role) in new[] { (db.First, "Professional"), (db.Second, "Professional"), (db.Student, "Medical_Student"), (db.Admin, "Admin") })
            {
                user.CreatedAt = created = created.AddMinutes(1);
                db.Context.Users.Add(user);
                db.Context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role });
            }
            for (var id = 1; id <= 3; id++)
            {
                db.Context.MedDatas.Add(new()
                {
                    Id = id, ImageUrl = $"https://images.example.test/{id}.png", Pmcid = $"PMC{id}",
                    Speciality = id <= 2 ? "Cardiology" : "dermatology", BodyRegion = "chest", Modality = "ct"
                });
            }
            db.Context.MedDataKeywords.AddRange(
                new MedDataKeyword { MedDataId = 1, Keyword = "lesion" }, new MedDataKeyword { MedDataId = 1, Keyword = "margin" },
                new MedDataKeyword { MedDataId = 2, Keyword = "lesion" });
            await db.Context.SaveChangesAsync();
            return db;
        }

        public Task<(MedData? MedData, string Counter)> AssignAsync(User user, bool student = false) =>
            Repository.GetNthMedDataBySpecialityAndPositionAsync(user.Speciality,
                student ? "medical student" : "professional", user.BodyRegion, user.ImageModality, user.Id);

        private static User Person(string id, string position, string speciality) => new()
        {
            Id = id, Email = id + "@example.test", UserName = id + "@example.test", FullName = id, Position = position,
            Speciality = speciality, BodyRegion = "chest", ImageModality = "ct"
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
