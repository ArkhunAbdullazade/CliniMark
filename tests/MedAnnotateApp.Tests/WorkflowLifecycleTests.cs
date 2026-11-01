using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Infrastructure.Data;
using MedAnnotateApp.Infrastructure.Repositories;
using MedAnnotateApp.Infrastructure.Services.Admin;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedAnnotateApp.Tests;

public sealed class WorkflowLifecycleTests
{
    private const string Coordinates = """
        {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"rectangle","points":[[10,20],[100,20],[100,80],[10,80]]}]}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedRoleQueueReturnsNoImageAndFullMatchingCounter(bool student)
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        foreach (var image in await database.Context.MedDatas.Where(image => image.Id <= 2).ToListAsync())
            if (student) image.IsAnnotatedByStudent = true;
            else image.IsAnnotated = true;
        await database.Context.SaveChangesAsync();

        var result = await database.AssignAsync(student ? database.Student : database.Owner, student);

        Assert.Null(result.MedData);
        Assert.Equal("2/2", result.Counter);
        var source = await database.Context.MedDatas.AsNoTracking().OrderBy(image => image.Id).ToListAsync();
        Assert.Equal(3, source.Count);
        Assert.All(source, image => { Assert.Null(image.LockedByUserId); Assert.Null(image.LockedByStudentUserId); });
        Assert.All(source.Take(2), image => Assert.False(student ? image.IsAnnotated : image.IsAnnotatedByStudent));
        Assert.False(source[2].IsAnnotated);
        Assert.False(source[2].IsAnnotatedByStudent);
        Assert.Equal(3, await database.Context.MedDataKeywords.CountAsync());
    }

    [Fact]
    public async Task SpecialistAndStudentCanIndependentlyOwnTheSameImage()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var specialist = await database.AssignAsync(database.Owner);
        var student = await database.AssignAsync(database.Student, true);
        var otherSpecialist = await database.AssignAsync(database.Other);

        Assert.Equal(1, specialist.MedData?.Id);
        Assert.Equal(1, student.MedData?.Id);
        Assert.Equal(2, otherSpecialist.MedData?.Id);
        var image = (await database.Context.MedDatas.AsNoTracking().SingleAsync(item => item.Id == 1));
        Assert.Equal(database.Owner.Id, image.LockedByUserId);
        Assert.Equal(database.Student.Id, image.LockedByStudentUserId);
        Assert.False(image.IsAnnotated);
        Assert.False(image.IsAnnotatedByStudent);
    }

    [Fact]
    public async Task SavingFinalTermStillRequiresNextImageToCompleteAndRelease()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        database.Context.MedDataKeywords.Add(new() { MedDataId = 1, Keyword = "boundary" });
        await database.Context.SaveChangesAsync();
        await database.AssignAsync(database.Owner);
        Assert.True((await database.Repository.SaveProfessionalAsync(database.Owner, Response("lesion"))).Success);
        Assert.False((await database.Repository.CompleteProfessionalAsync(1, database.Owner)).Success);
        Assert.True((await database.Repository.SaveProfessionalAsync(database.Owner, Response("boundary"))).Success);
        var beforeNext = (await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1));
        Assert.False(beforeNext.IsAnnotated);
        Assert.Equal(database.Owner.Id, beforeNext.LockedByUserId);

        Assert.True((await database.Repository.CompleteProfessionalAsync(1, database.Owner)).Success);

        var completed = (await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1));
        Assert.True(completed.IsAnnotated);
        Assert.Null(completed.LockedByUserId);
        Assert.Null(completed.KeywordStates);
        Assert.Equal(2, await database.Context.AnnotatedMedDatas.CountAsync());
        var next = await database.AssignAsync(database.Owner);
        Assert.Equal(2, next.MedData?.Id);
        Assert.Equal("1/2", next.Counter);
    }

    [Fact]
    public async Task AbandonedUnstartedLockPersistsUntilItsOwnerReturnsOrReleasesIt()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        await database.AssignAsync(database.Owner);

        // A fresh request context represents returning after the browser disappeared.
        await using var fresh = database.NewContext();
        var freshRepository = new MedDataRepository(fresh);
        var other = await freshRepository.GetNthMedDataBySpecialityAndPositionAsync(
            "cardiology", "professional", "chest", "ct", database.Other.Id);
        var resumed = await freshRepository.GetNthMedDataBySpecialityAndPositionAsync(
            "cardiology", "professional", "chest", "ct", database.Owner.Id);

        Assert.Equal(2, other.MedData?.Id);
        Assert.Equal(1, resumed.MedData?.Id);
        Assert.Equal(database.Owner.Id, (await fresh.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1)).LockedByUserId);
        Assert.Empty(await fresh.AnnotatedMedDatas.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutReleasesAnEmptyAssignmentDespiteFabricatedBrowserProgress(bool student)
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var owner = student ? database.Student : database.Owner;
        await database.AssignAsync(owner, student);

        Assert.True(await database.Repository.UpdateLock(1, "[2]", true, student, owner.Id));

        var released = await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1);
        Assert.Null(released.LockedByUserId);
        Assert.Null(released.LockedByStudentUserId);
        Assert.Null(released.KeywordStates);
        Assert.False(released.IsAnnotated);
        Assert.False(released.IsAnnotatedByStudent);
        Assert.Equal(1, (await database.AssignAsync(database.Other, student)).MedData?.Id);
    }

    [Fact]
    public async Task LockTimesFollowEachLockAndSavedRowsAreTimestamped()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);
        await database.AssignAsync(database.Owner);
        await database.AssignAsync(database.Student, true);
        var claimed = await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1);
        Assert.True(claimed.LockedAt >= before);
        Assert.True(claimed.StudentLockedAt >= before);

        // Returning to the page refreshes the time, so only abandoned locks look idle to the sweeper.
        var stale = await database.Context.MedDatas.SingleAsync(image => image.Id == 1);
        stale.LockedAt = DateTime.UtcNow.AddDays(-2);
        await database.Context.SaveChangesAsync();
        await database.AssignAsync(database.Owner);
        Assert.True((await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1)).LockedAt >= before);

        Assert.True((await database.Repository.SaveProfessionalAsync(database.Owner, Response("lesion"))).Success);
        Assert.True((await database.Repository.CompleteProfessionalAsync(1, database.Owner)).Success);
        Assert.True((await database.Repository.SaveStudentAsync(database.Student,
            [new AnnotatedByStudentsMedData { MedDataId = 1, Coordinates = Coordinates, TextualAnnotation = "lesion" }])).Success);

        var completed = await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1);
        Assert.Null(completed.LockedAt);
        Assert.Null(completed.StudentLockedAt);
        Assert.True((await database.Context.AnnotatedMedDatas.SingleAsync()).CreatedAt >= before);
        Assert.True((await database.Context.AnnotatedByStudentsMedDatas.SingleAsync()).CreatedAt >= before);
    }

    [Fact]
    public async Task ReleasingAnEmptyLockClearsItsTime()
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        await database.AssignAsync(database.Owner);

        Assert.True(await database.Repository.UpdateLock(1, "", false, false, database.Owner.Id));

        Assert.Null((await database.Context.MedDatas.AsNoTracking().SingleAsync(image => image.Id == 1)).LockedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentStatusFollowsTheHolderUntilTheStudyTeamReleasesIt(bool student)
    {
        await using var database = await LifecycleDatabase.CreateAsync();
        var owner = student ? database.Student : database.Owner;
        Assert.False(await database.Repository.RefreshAssignmentAsync(1, owner.Id, student));
        await database.AssignAsync(owner, student);

        Assert.True(await database.Repository.RefreshAssignmentAsync(1, owner.Id, student));
        Assert.False(await database.Repository.RefreshAssignmentAsync(1, owner.Id, !student));
        Assert.False(await database.Repository.RefreshAssignmentAsync(1, database.Other.Id, student));
        Assert.False(await database.Repository.RefreshAssignmentAsync(2, owner.Id, student));

        var release = await new AssignmentAdminService(database.Context).ReleaseAsync(1, student, owner.Id, database.Other.Id);
        Assert.True(release.Released);
        Assert.False(await database.Repository.RefreshAssignmentAsync(1, owner.Id, student));
    }

    private static AnnotatedMedData Response(string keyword) => new()
    {
        MedDataId = 1, ExtractedKeyword = keyword, PressedButton = "Save and Next Term", BoxCoordinates = Coordinates
    };

    private sealed class LifecycleDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MedDataDbContext Context { get; }
        public MedDataRepository Repository { get; }
        public User Owner { get; } = Researcher("owner", "specialist");
        public User Other { get; } = Researcher("other", "specialist");
        public User Student { get; } = Researcher("student", "medical student");

        private LifecycleDatabase(SqliteConnection connection)
        {
            this.connection = connection;
            Context = NewContext();
            Repository = new(Context);
        }

        public MedDataDbContext NewContext() => new(new DbContextOptionsBuilder<MedDataDbContext>().UseSqlite(connection).Options);

        public static async Task<LifecycleDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new LifecycleDatabase(connection);
            await database.Context.Database.EnsureCreatedAsync();
            database.Context.Users.AddRange(database.Owner, database.Other, database.Student);
            for (var id = 1; id <= 3; id++)
            {
                database.Context.MedDatas.Add(new()
                {
                    Id = id, ImageUrl = $"https://images.example.test/{id}.png",
                    Speciality = id <= 2 ? "cardiology" : "dermatology", BodyRegion = "chest", Modality = "ct"
                });
                database.Context.MedDataKeywords.Add(new() { MedDataId = id, Keyword = "lesion" });
            }
            await database.Context.SaveChangesAsync();
            return database;
        }

        public Task<(MedData? MedData, string Counter)> AssignAsync(User user, bool student = false) =>
            Repository.GetNthMedDataBySpecialityAndPositionAsync(user.Speciality,
                student ? "medical student" : "professional", user.BodyRegion, user.ImageModality, user.Id);

        private static User Researcher(string id, string position) => new()
        {
            Id = id, Email = id + "@example.test", Position = position,
            Speciality = "cardiology", BodyRegion = "chest", ImageModality = "ct"
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
