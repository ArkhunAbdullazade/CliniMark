using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CliniMark.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLINIMARK_TEST_POSTGRES_CONNECTION")))
            Skip = "Set CLINIMARK_TEST_POSTGRES_CONNECTION to an isolated localhost clinimark_audit_ database to run PostgreSQL concurrency checks.";
    }
}

public sealed class PostgreSqlWorkflowTests
{
    private const string Coordinates = """
        {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"rectangle","points":[[10,20],[100,20],[100,80],[10,80]]}]}
        """;

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task PostgreSqlQueriesClaimsRetriesAndSaveRollbackAreCorrect()
    {
        var configured = Environment.GetEnvironmentVariable("CLINIMARK_TEST_POSTGRES_CONNECTION")!;
        var builder = new NpgsqlConnectionStringBuilder(configured);
        Assert.StartsWith("clinimark_audit_", builder.Database);
        Assert.Contains(builder.Host, new[] { "localhost", "127.0.0.1", "::1" });
        var schema = "audit_workflow_" + Guid.NewGuid().ToString("N");
        await using var setup = new NpgsqlConnection(builder.ConnectionString);
        await setup.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync();
        builder.SearchPath = schema;
        var options = new DbContextOptionsBuilder<MedDataDbContext>().UseNpgsql(builder.ConnectionString).Options;
        try
        {
            await using (var seed = new MedDataDbContext(options))
            {
                await seed.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                for (var id = 1; id <= 10; id++) seed.Users.Add(Researcher(id));
                for (var id = 1; id <= 5; id++)
                {
                    seed.MedDatas.Add(new()
                    {
                        Id = id, ImageUrl = $"https://example.test/{id}.png",
                        Speciality = id == 5 ? null : id % 2 == 1 ? " CARDIOLOGY " : "dermatology",
                        BodyRegion = id % 2 == 1 ? "thorax" : "head and neck", Modality = id % 2 == 1 ? "ct" : "mri"
                    });
                    seed.MedDataKeywords.Add(new() { MedDataId = id, Keyword = "lesion" });
                }
                await seed.SaveChangesAsync();
            }

            // Two requests per account, twenty overlapping claims, four matching images.
            var claims = await Task.WhenAll(Enumerable.Range(1, 10).SelectMany(user => Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var context = new MedDataDbContext(options);
                var researcher = Researcher(user);
                var (image, counter) = await new MedDataRepository(context).GetNthMedDataBySpecialityAndPositionAsync(
                    researcher.Speciality, "professional", researcher.BodyRegion, researcher.ImageModality, researcher.Id);
                Assert.Equal("0/4", counter);
                return (researcher.Id, ImageId: image?.Id);
            })));
            var successful = claims.Where(item => item.ImageId.HasValue).ToList();
            Assert.Equal(4, successful.Select(item => item.ImageId).Distinct().Count());
            Assert.All(successful.GroupBy(item => item.ImageId), group => Assert.Single(group.Select(item => item.Id).Distinct()));
            Assert.All(successful.GroupBy(item => item.Id), group => Assert.Single(group.Select(item => item.ImageId).Distinct()));
            var claim = successful[0];
            var ownerNumber = int.Parse(claim.Id["researcher-".Length..]);
            var owner = Researcher(ownerNumber);
            var imageId = claim.ImageId!.Value;

            var saves = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var context = new MedDataDbContext(options);
                return await new MedDataRepository(context).SaveProfessionalAsync(owner, Response(imageId));
            }));
            Assert.All(saves, result => Assert.True(result.Success, result.Message));
            await using (var verify = new MedDataDbContext(options))
            {
                Assert.Equal(1, await verify.AnnotatedMedDatas.CountAsync());
                Assert.Equal(Coordinates, (await verify.AnnotatedMedDatas.SingleAsync()).BoxCoordinates);
                Assert.False((await new MedDataRepository(verify).SaveProfessionalAsync(Researcher(999), Response(imageId))).Success);
            }

            // Deliberately fail the second database write to prove the annotation insert also rolls back.
            var unsaved = successful.First(item => item.ImageId != imageId);
            var nextOwner = Researcher(int.Parse(unsaved.Id["researcher-".Length..]));
            await using (var fail = new MedDataDbContext(options))
            {
                await fail.Database.OpenConnectionAsync();
                await using var injectFailure = new NpgsqlCommand($"""
                    CREATE FUNCTION "{schema}".reject_progress() RETURNS trigger LANGUAGE plpgsql AS $body$
                    BEGIN RAISE EXCEPTION 'Intentional isolated test failure'; END; $body$;
                    CREATE TRIGGER reject_progress BEFORE UPDATE OF "KeywordStates" ON "{schema}"."MedDatas"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}".reject_progress();
                    """, (NpgsqlConnection)fail.Database.GetDbConnection());
                await injectFailure.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<DbUpdateException>(() => new MedDataRepository(fail).SaveProfessionalAsync(nextOwner, Response(unsaved.ImageId!.Value)));
            }
            await using (var verify = new MedDataDbContext(options))
            {
                Assert.Equal(1, await verify.AnnotatedMedDatas.CountAsync());
                Assert.Equal("[3]", (await verify.MedDatas.FindAsync(unsaved.ImageId!.Value))!.KeywordStates);
                Assert.Equal(nextOwner.Id, (await verify.MedDatas.FindAsync(unsaved.ImageId!.Value))!.LockedByUserId);
            }
        }
        finally
        {
            // Delete only this test's newly created GUID schema in the explicitly isolated test database.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static User Researcher(int id) => new()
    {
        Id = "researcher-" + id, Email = $"researcher{id}@example.test", Speciality = "cardiology,dermatology",
        BodyRegion = "thorax,head and neck", ImageModality = "ct,mri"
    };

    private static AnnotatedMedData Response(int imageId) => new()
    {
        MedDataId = imageId, BoxCoordinates = Coordinates, ExtractedKeyword = "lesion", PressedButton = "Save and Next Term"
    };
}
