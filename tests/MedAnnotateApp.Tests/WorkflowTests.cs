using System.Text.Json;
using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Infrastructure.Data;
using MedAnnotateApp.Infrastructure.Repositories;
using MedAnnotateApp.Presentation.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedAnnotateApp.Tests;

public sealed class WorkflowTests
{
    private const string Coordinates = """
        {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"rectangle","points":[[10.125,20.25],[100.125,20.25],[100.125,80.25],[10.125,80.25]]}]}
        """;

    // Images: 1 cardiology/chest/ct, 2 dermatology/head/mri, 3 cardiology/abdomen/ct, 4 cardiology/chest/xray, 5 no metadata.
    [Theory]
    [InlineData(null, null, "ct", 1, "0/2")]
    [InlineData(null, "head", null, 2, "0/1")]
    [InlineData("cardiology", null, null, 1, "0/3")]
    [InlineData(" , ", "chest", "xray", 4, "0/1")]
    public async Task OnlyTheChosenListsRestrictTheImages(string? speciality, string? region, string? modality, int expected, string counter)
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        var (image, progress) = await db.Repository.GetNthMedDataBySpecialityAndPositionAsync(speciality, "professional", region, modality, db.Owner.Id);
        Assert.Equal(expected, image!.Id);
        Assert.Equal(counter, progress);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(" , ", "", null)]
    public async Task AnEmptySelectionCannotClaimAnImage(string? speciality, string? region, string? modality)
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        var (image, _) = await db.Repository.GetNthMedDataBySpecialityAndPositionAsync(speciality, "professional", region, modality, db.Owner.Id);
        Assert.Null(image);
        Assert.All(await db.Context.MedDatas.ToListAsync(), item => Assert.Null(item.LockedByUserId));
    }

    [Fact]
    public async Task MultipleSelectionsAreOrWithinEachListAndAndBetweenLists()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        db.Owner.Speciality = " CARDIOLOGY , dermatology,cardiology";
        db.Owner.BodyRegion = "chest,head";
        db.Owner.ImageModality = "ct,mri";
        var (first, firstCounter) = await db.AssignAsync();
        Assert.Equal(1, first?.Id);
        Assert.Equal("0/2", firstCounter);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
        var (second, secondCounter) = await db.AssignAsync();
        Assert.Equal(2, second?.Id);
        Assert.Equal("1/2", secondCounter);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(2))).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(2, db.Owner)).Success);
        var (none, finalCounter) = await db.AssignAsync();
        Assert.Null(none);
        Assert.Equal("2/2", finalCounter);
    }

    [Fact]
    public async Task ExistingLockCannotBypassNewFiltersAndUnlabelledImagesNeverMatch()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        db.Owner.Speciality = "dermatology";
        db.Owner.BodyRegion = "head";
        db.Owner.ImageModality = "mri";
        var (image, counter) = await db.AssignAsync();
        Assert.Equal(2, image?.Id);
        Assert.Equal("0/1", counter);
        Assert.Null((await db.Context.MedDatas.FindAsync(1))!.LockedByUserId);
        db.Owner.Speciality = "unmatched";
        Assert.Null((await db.AssignAsync()).MedData);
        Assert.Null((await db.Context.MedDatas.FindAsync(5))!.LockedByUserId);
    }

    [Fact]
    public async Task ReloadReturnsSameAssignmentAndAnotherUserCannotClaimIt()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        var first = (await db.AssignAsync()).MedData;
        Assert.Equal(first?.Id, (await db.AssignAsync()).MedData?.Id);
        var second = await db.Repository.GetNthMedDataBySpecialityAndPositionAsync("cardiology", "professional", "chest", "ct", db.Other.Id);
        Assert.Null(second.MedData);
    }

    [Fact]
    public async Task SavingUsesDatabaseMetadataAndPersistsExactCoordinateRoundTrip()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        var response = Response(1);
        response.ImageUrl = "https://attacker.invalid/wrong-image";
        response.Email = db.Other.Email;
        response.Diagnosis = "invented";
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, response)).Success);
        db.Context.ChangeTracker.Clear();
        var saved = await db.Context.AnnotatedMedDatas.SingleAsync();
        Assert.Equal("https://images.example.test/1.png", saved.ImageUrl);
        Assert.Equal(db.Owner.Email, saved.Email);
        Assert.Equal("source diagnosis", saved.Diagnosis);
        Assert.Equal(Coordinates, saved.BoxCoordinates);
        using var coordinates = JsonDocument.Parse(saved.BoxCoordinates!);
        Assert.Equal(10.125, coordinates.RootElement.GetProperty("annotations")[0].GetProperty("points")[0][0].GetDouble());
    }

    [Fact]
    public async Task OneKeywordPreservesTwoRectanglesAndAFreehandInOneSavedResponse()
    {
        const string mixedCoordinates = """
            {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"rectangle","points":[[10.125,20.25],[100.125,20.25],[100.125,80.25],[10.125,80.25]]},{"type":"rectangle","points":[[300,100],[360,160],[330,190],[270,130]]},{"type":"freehand","points":[[500,100],[550,110],[570,150],[525,175],[490,140],[500,100]]}]}
            """;
        Assert.True(AnnotationCoordinates.TryValidate(mixedCoordinates, out var error), error);
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        var response = Response(1);
        response.BoxCoordinates = mixedCoordinates;
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, response)).Success);
        db.Context.ChangeTracker.Clear();

        var saved = Assert.Single((await db.Repository.GetProfessionalAnnotationsAsync(1, db.Owner))!);
        Assert.Equal("lesion", saved.ExtractedKeyword);
        Assert.Equal(1, saved.MedDataId);
        Assert.Equal(mixedCoordinates, saved.BoxCoordinates);
        using var coordinates = JsonDocument.Parse(saved.BoxCoordinates!);
        var shapes = coordinates.RootElement.GetProperty("annotations").EnumerateArray().ToArray();
        Assert.Equal(new[] { "rectangle", "rectangle", "freehand" }, shapes.Select(shape => shape.GetProperty("type").GetString()!).ToArray());
        Assert.Equal(6, shapes[2].GetProperty("points").GetArrayLength());
        Assert.Equal(1, await db.Context.AnnotatedMedDatas.CountAsync());
    }

    [Fact]
    public async Task DoubleSubmitDoesNotDuplicateAndStaleDifferentResponseCannotOverwrite()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        var retry = Response(1);
        retry.Timestamps = "(2000,8000)";
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, retry)).Success);
        var changed = Response(1);
        changed.Comment = "stale second tab";
        var conflict = await db.Repository.SaveProfessionalAsync(db.Owner, changed);
        Assert.False(conflict.Success);
        Assert.True(conflict.Conflict);
        Assert.Equal(1, await db.Context.AnnotatedMedDatas.CountAsync());
        Assert.Null((await db.Context.AnnotatedMedDatas.SingleAsync()).Comment);
    }

    [Fact]
    public async Task TechnicalProblemIsSavedWithTheTermAndCannotBeChangedByAResubmission()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        var report = Response(1);
        report.TechnicalProblem = "The image loaded blurred";
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, report)).Success);
        var retry = Response(1);
        retry.TechnicalProblem = "The image loaded blurred";
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, retry)).Success);
        var changed = Response(1);
        changed.TechnicalProblem = "A different report from a second tab";
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, changed)).Conflict);
        var tooLong = Response(1);
        tooLong.ExtractedKeyword = "boundary";
        tooLong.TechnicalProblem = new string('x', 4001);
        Assert.False((await db.Repository.SaveProfessionalAsync(db.Owner, tooLong)).Success);
        db.Context.ChangeTracker.Clear();
        var saved = Assert.Single(await db.Context.AnnotatedMedDatas.ToListAsync());
        Assert.Equal("The image loaded blurred", saved.TechnicalProblem);
        Assert.Null(saved.Comment);
    }

    private const string CustomContour = """
        {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"freehand","points":[[200,100],[300,100],[300,200],[200,200],[200,100]]}]}
        """;

    [Fact]
    public async Task CustomAnnotationsAreSavedSeparatelyRepeatSafeAndOnlyByTheHolderBeforeCompletion()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        var (first, firstId) = await db.Repository.SaveCustomAnnotationAsync(db.Owner, 1, "  periapical lucency ", CustomContour);
        Assert.True(first.Success);
        var (again, againId) = await db.Repository.SaveCustomAnnotationAsync(db.Owner, 1, "periapical lucency", CustomContour);
        Assert.True(again.Success);
        Assert.Equal(firstId, againId);
        Assert.True((await db.Repository.SaveCustomAnnotationAsync(db.Owner, 1, "second finding", CustomContour)).Result.Success);
        Assert.False((await db.Repository.SaveCustomAnnotationAsync(db.Owner, 1, "   ", CustomContour)).Result.Success);
        Assert.False((await db.Repository.SaveCustomAnnotationAsync(db.Owner, 1, "broken", "{\"version\":1}")).Result.Success);
        Assert.True((await db.Repository.SaveCustomAnnotationAsync(db.Other, 1, "not theirs", CustomContour)).Result.Conflict);
        Assert.True((await db.Repository.DeleteCustomAnnotationAsync(db.Other, firstId!.Value)).Success);

        db.Context.ChangeTracker.Clear();
        var saved = await db.Context.CustomAnnotations.OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(["periapical lucency", "second finding"], saved.Select(item => item.Term));
        Assert.All(saved, item => Assert.Equal(db.Owner.Id, item.UserId));
        Assert.Empty(await db.Context.AnnotatedMedDatas.ToListAsync());
        Assert.Equal(2, (await db.Repository.GetCustomAnnotationsAsync(1, db.Owner))!.Count);
        Assert.Null(await db.Repository.GetCustomAnnotationsAsync(1, db.Other));

        Assert.True((await db.Repository.DeleteCustomAnnotationAsync(db.Owner, saved[1].Id)).Success);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
        // Completed work stays as saved: no more custom annotations, and none can be deleted.
        Assert.True((await db.Repository.SaveCustomAnnotationAsync(db.Owner, 1, "late", CustomContour)).Result.Conflict);
        Assert.True((await db.Repository.DeleteCustomAnnotationAsync(db.Owner, saved[0].Id)).Conflict);
        db.Context.ChangeTracker.Clear();
        Assert.Equal("periapical lucency", (await db.Context.CustomAnnotations.SingleAsync()).Term);
    }

    [Fact]
    public async Task OtherUserCannotSaveCompleteReadOrUnlockOwnedImage()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        Assert.False((await db.Repository.SaveProfessionalAsync(db.Other, Response(1))).Success);
        Assert.False((await db.Repository.CompleteProfessionalAsync(1, db.Other)).Success);
        Assert.Null(await db.Repository.GetProfessionalAnnotationsAsync(1, db.Other));
        Assert.False(await db.Repository.UpdateLock(1, "[2]", false, false, db.Other.Id));
        Assert.Equal(db.Owner.Id, (await db.Context.MedDatas.FindAsync(1))!.LockedByUserId);
        Assert.Equal(0, await db.Context.AnnotatedMedDatas.CountAsync());
    }

    [Fact]
    public async Task CannotCompleteBeforeEverySourceTermHasSavedResponse()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        db.Context.MedDataKeywords.Add(new() { MedDataId = 1, Keyword = "second term" });
        await db.Context.SaveChangesAsync();
        await db.AssignAsync();
        Assert.False((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        Assert.False((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
        var nonVisible = Response(1);
        nonVisible.ExtractedKeyword = "second term";
        nonVisible.PressedButton = "Not Visible/Abstract";
        nonVisible.BoxCoordinates = "";
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, nonVisible)).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
        Assert.True((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
        Assert.True((await db.Context.MedDatas.FindAsync(1))!.IsAnnotated);
    }

    [Theory]
    [InlineData("other image's term", "Save and Next Term", Coordinates)]
    [InlineData("lesion", "arbitrary action", Coordinates)]
    [InlineData("lesion", "Save and Next Term", "[]")]
    [InlineData("lesion", "Save and Next Term", "")]
    [InlineData("lesion", "Uncertain/Skip", Coordinates)]
    public async Task InvalidOrIncompleteAnnotationIsNotSaved(string keyword, string action, string coordinates)
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        var response = Response(1);
        response.ExtractedKeyword = keyword;
        response.PressedButton = action;
        response.BoxCoordinates = coordinates;
        Assert.False((await db.Repository.SaveProfessionalAsync(db.Owner, response)).Success);
        Assert.Equal(0, await db.Context.AnnotatedMedDatas.CountAsync());
        Assert.False((await db.Context.MedDatas.FindAsync(1))!.IsAnnotated);
    }

    [Fact]
    public async Task SavedProgressSurvivesLogoutReloadAndFilterChanges()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        db.Context.MedDataKeywords.Add(new() { MedDataId = 1, Keyword = "second term" });
        await db.Context.SaveChangesAsync();
        await db.AssignAsync();
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        Assert.True(await db.Repository.UpdateLock(1, "[1,1]", false, false, db.Owner.Id));
        Assert.Equal(db.Owner.Id, (await db.Context.MedDatas.FindAsync(1))!.LockedByUserId);
        Assert.Equal("[2,3]", (await db.AssignAsync()).MedData!.KeywordStates);
        Assert.Single((await db.Repository.GetProfessionalAnnotationsAsync(1, db.Owner))!);
        await db.Repository.ReleaseAssignmentsAsync(db.Owner.Id, false);
        db.Owner.Speciality = "dermatology";
        db.Owner.BodyRegion = "head";
        db.Owner.ImageModality = "mri";
        Assert.Equal(2, (await db.AssignAsync()).MedData?.Id);
        Assert.Equal(db.Owner.Id, (await db.Context.MedDatas.FindAsync(1))!.LockedByUserId);
        db.Owner.Speciality = "cardiology";
        db.Owner.BodyRegion = "chest";
        db.Owner.ImageModality = "ct";
        Assert.Equal(1, (await db.AssignAsync()).MedData?.Id);
    }

    [Fact]
    public async Task StudentSaveAndCompletionAreAtomicAndRetryIsIdempotent()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        (await db.Context.MedDatas.FindAsync(1))!.SkinTone = "III";
        await db.Context.SaveChangesAsync();
        await db.Repository.GetNthMedDataBySpecialityAndPositionAsync("cardiology", "medical student", "chest", "ct", db.Owner.Id);
        var first = new AnnotatedByStudentsMedData { MedDataId = 1, Coordinates = Coordinates, TextualAnnotation = "lesion", ImageUrl = "tampered" };
        Assert.True((await db.Repository.SaveStudentAsync(db.Owner, [first])).Success);
        var retry = new AnnotatedByStudentsMedData { MedDataId = 1, Coordinates = Coordinates, TextualAnnotation = "lesion" };
        Assert.True((await db.Repository.SaveStudentAsync(db.Owner, [retry])).Success);
        Assert.Equal(1, await db.Context.AnnotatedByStudentsMedDatas.CountAsync());
        Assert.True((await db.Context.MedDatas.FindAsync(1))!.IsAnnotatedByStudent);
        Assert.Null((await db.Context.MedDatas.FindAsync(1))!.LockedByStudentUserId);
        Assert.Equal("https://images.example.test/1.png", (await db.Context.AnnotatedByStudentsMedDatas.SingleAsync()).ImageUrl);
        Assert.Equal("III", (await db.Context.AnnotatedByStudentsMedDatas.SingleAsync()).SkinTone);
    }

    [Fact]
    public async Task StudentBatchCannotMixImagesOrIncludeInvalidGroup()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.Repository.GetNthMedDataBySpecialityAndPositionAsync("cardiology", "medical student", "chest", "ct", db.Owner.Id);
        var valid = new AnnotatedByStudentsMedData { MedDataId = 1, Coordinates = Coordinates, TextualAnnotation = "lesion" };
        var differentImage = new AnnotatedByStudentsMedData { MedDataId = 2, Coordinates = Coordinates, TextualAnnotation = "other" };
        Assert.False((await db.Repository.SaveStudentAsync(db.Owner, [valid, differentImage])).Success);
        differentImage.MedDataId = 1;
        differentImage.Coordinates = "[]";
        Assert.False((await db.Repository.SaveStudentAsync(db.Owner, [valid, differentImage])).Success);
        Assert.Equal(0, await db.Context.AnnotatedByStudentsMedDatas.CountAsync());
        Assert.False((await db.Context.MedDatas.FindAsync(1))!.IsAnnotatedByStudent);
        Assert.Equal(db.Owner.Id, (await db.Context.MedDatas.FindAsync(1))!.LockedByStudentUserId);
    }

    [Fact]
    public async Task AllGroupsAndNewResponsesMustUseConsistentOriginalImageDimensions()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        Assert.True((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        await db.Repository.GetNthMedDataBySpecialityAndPositionAsync("cardiology", "medical student", "chest", "ct", db.Owner.Id);
        var wrongDimensions = new AnnotatedByStudentsMedData
        {
            MedDataId = 1, TextualAnnotation = "lesion", Coordinates = Coordinates.Replace("\"imageWidth\":1000", "\"imageWidth\":2000")
        };
        Assert.False((await db.Repository.SaveStudentAsync(db.Owner, [wrongDimensions])).Success);
        Assert.Equal(0, await db.Context.AnnotatedByStudentsMedDatas.CountAsync());
        var correct = new AnnotatedByStudentsMedData { MedDataId = 1, TextualAnnotation = "lesion", Coordinates = Coordinates };
        Assert.True((await db.Repository.SaveStudentAsync(db.Owner, [correct])).Success);
    }

    [Fact]
    public async Task ChangedFiltersAlsoBlockPostingAnOldAssignment()
    {
        await using var db = await WorkflowDatabase.CreateAsync();
        await db.AssignAsync();
        db.Owner.BodyRegion = "head";
        Assert.False((await db.Repository.SaveProfessionalAsync(db.Owner, Response(1))).Success);
        Assert.False((await db.Repository.CompleteProfessionalAsync(1, db.Owner)).Success);
    }

    [Fact]
    public void SpecialistAndStudentEndpointsRequireTheCorrectRoles()
    {
        foreach (var method in new[] { nameof(MedDataController.ProcessAnnotatedMedData), nameof(MedDataController.NextImage), nameof(MedDataController.CurrentAnnotations) })
            Assert.Contains(typeof(MedDataController).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>(), item => item.Roles == "Professional");
        Assert.Contains(typeof(MedDataController).GetMethod(nameof(MedDataController.SubmitStudentAnnotations))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>(), item => item.Roles == "Medical_Student");
    }

    private static AnnotatedMedData Response(int id) => new()
    {
        MedDataId = id, BoxCoordinates = Coordinates, ExtractedKeyword = "lesion", PressedButton = "Save and Next Term", Timestamps = "(100,200)"
    };

    private sealed class WorkflowDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MedDataDbContext Context { get; }
        public MedDataRepository Repository { get; }
        public User Owner { get; } = new() { Id = "owner", Email = "specialist@example.test", Speciality = "cardiology", BodyRegion = "chest", ImageModality = "ct" };
        public User Other { get; } = new() { Id = "other", Email = "other@example.test", Speciality = "cardiology", BodyRegion = "chest", ImageModality = "ct" };

        private WorkflowDatabase(SqliteConnection connection)
        {
            this.connection = connection;
            Context = new(new DbContextOptionsBuilder<MedDataDbContext>().UseSqlite(connection).Options);
            Repository = new(Context);
        }

        public static async Task<WorkflowDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new WorkflowDatabase(connection);
            await db.Context.Database.EnsureCreatedAsync();
            db.Context.Users.AddRange(db.Owner, db.Other);
            db.Context.MedDatas.AddRange(Image(1, "cardiology", "chest", "ct"), Image(2, "dermatology", "head", "mri"),
                Image(3, "cardiology", "abdomen", "ct"), Image(4, "cardiology", "chest", "xray"), Image(5, null, null, null));
            for (var id = 1; id <= 5; id++) db.Context.MedDataKeywords.Add(new() { MedDataId = id, Keyword = "lesion" });
            await db.Context.SaveChangesAsync();
            return db;
        }

        public Task<(MedData? MedData, string Counter)> AssignAsync() => Repository.GetNthMedDataBySpecialityAndPositionAsync(
            Owner.Speciality, "professional", Owner.BodyRegion, Owner.ImageModality, Owner.Id);

        private static MedData Image(int id, string? speciality, string? region, string? modality) => new()
        {
            Id = id, ImageUrl = $"https://images.example.test/{id}.png", Speciality = speciality, BodyRegion = region, Modality = modality, Diagnosis = "source diagnosis"
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
