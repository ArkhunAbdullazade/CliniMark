using System.IO.Compression;
using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Repositories;
using CliniMark.Infrastructure.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CliniMark.Tests;

public sealed class PostgreSqlAdminTests
{
    private const string Coordinates = """
        {"version":1,"coordinateSystem":"image-pixels","imageWidth":1000,"imageHeight":500,"annotations":[{"type":"rectangle","points":[[10,20],[100,20],[100,80],[10,80]]}]}
        """;

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task AdminQueriesReleasesSweepAndExportsRunOnPostgreSql()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLINIMARK_TEST_POSTGRES_CONNECTION")!);
        Assert.StartsWith("clinimark_audit_", builder.Database);
        Assert.Contains(builder.Host, new[] { "localhost", "127.0.0.1", "::1" });
        var schema = "audit_admin_" + Guid.NewGuid().ToString("N");
        await using var setup = new NpgsqlConnection(builder.ConnectionString);
        await setup.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync();
        builder.SearchPath = schema;
        var options = new DbContextOptionsBuilder<MedDataDbContext>().UseNpgsql(builder.ConnectionString).Options;
        try
        {
            var specialist = Person("spec", "specialist");
            var student = Person("stud", "medical student");
            await using (var seed = new MedDataDbContext(options))
            {
                await seed.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                seed.Roles.AddRange(new IdentityRole("Professional") { Id = "p" }, new IdentityRole("Medical_Student") { Id = "s" });
                seed.Users.AddRange(specialist, student);
                seed.UserRoles.AddRange(new IdentityUserRole<string> { UserId = specialist.Id, RoleId = "p" },
                    new IdentityUserRole<string> { UserId = student.Id, RoleId = "s" });
                for (var id = 1; id <= 3; id++)
                    seed.MedDatas.Add(new() { Id = id, ImageUrl = $"https://example.test/{id}.png", Speciality = " Cardiology ", BodyRegion = "thorax", Modality = "ct" });
                seed.MedDataKeywords.AddRange(new MedDataKeyword { MedDataId = 1, Keyword = "lesion" }, new MedDataKeyword { MedDataId = 1, Keyword = "margin" },
                    new MedDataKeyword { MedDataId = 2, Keyword = "lesion" });
                await seed.SaveChangesAsync();
            }

            await using var context = new MedDataDbContext(options);
            var repository = new MedDataRepository(context);
            await repository.GetNthMedDataBySpecialityAndPositionAsync(specialist.Speciality, "professional", specialist.BodyRegion, specialist.ImageModality, specialist.Id);
            await repository.GetNthMedDataBySpecialityAndPositionAsync(student.Speciality, "medical student", student.BodyRegion, student.ImageModality, student.Id);
            Assert.True((await repository.SaveProfessionalAsync(specialist, new AnnotatedMedData
            {
                MedDataId = 1, ExtractedKeyword = "lesion", PressedButton = "Save and Next Term", BoxCoordinates = Coordinates, Timestamps = "(900,2100)"
            })).Success);

            var queries = new AdminQueryService(context, new MemoryCache(new MemoryCacheOptions()), Options.Create(new AdminOptions()));
            var overview = await queries.GetOverviewAsync();
            Assert.Equal(3, overview.TotalImages);
            Assert.Equal(1, overview.PartialLocks);
            Assert.Equal(2, overview.ActiveNow);
            Assert.Equal(2, (await queries.GetLocksAsync()).Count);
            Assert.Equal(3, (await queries.GetImagesAsync(new ImageQuery { Speciality = "cardiology" })).Total);
            Assert.Single((await queries.GetImagesAsync(new ImageQuery { SpecialistStatus = "partial" })).Items);
            Assert.Single((await queries.GetImagesAsync(new ImageQuery { Search = "marg" })).Items);
            Assert.Equal(2, (await queries.GetParticipantsAsync()).Count);
            Assert.NotNull(await queries.GetParticipantAsync(specialist.Id));
            Assert.NotNull(await queries.GetImageAsync(1));
            Assert.Single((await queries.GetResponsesAsync(new ResponseQuery
            {
                Completeness = Completeness.IncompleteOnly, From = DateTime.Today.AddDays(-1), To = DateTime.Today, Speciality = "cardiology"
            })).Items);
            Assert.Empty((await queries.GetResponsesAsync(new ResponseQuery { Completeness = Completeness.CompleteOnly })).Items);
            Assert.Equal(["cardiology"], (await queries.GetCategoryOptionsAsync()).Specialities);

            var assignments = new AssignmentAdminService(context);
            Assert.Equal(0, await assignments.ReleaseIdleEmptyLocksAsync(TimeSpan.FromHours(12), DateTime.UtcNow));
            Assert.Equal(1, await assignments.ReleaseIdleEmptyLocksAsync(TimeSpan.FromHours(12), DateTime.UtcNow.AddHours(13)));
            var released = await assignments.ReleaseManyAsync([(1, false, specialist.Id)], "admin");
            Assert.Equal(1, released[0].KeptResponses);
            Assert.Equal(2, (await queries.GetActionsAsync(1)).Total);

            var export = new ResultsExportService(context);
            await using var archive = await export.CreateArchiveAsync(new ExportOptions { Identity = IdentityMode.Full });
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
            Assert.Equal(12, zip.Entries.Count); // Includes custom_annotations, metadata_checks, problem_reports, image_skips, pixel_masks and full_data.
            await using var csv = await export.CreateResponsesCsvAsync(new ResponseQuery { AnnotatorId = specialist.Id }, IdentityMode.None);
            Assert.True(csv.Length > 0);
            Assert.NotEmpty((await queries.GetFullDataAsync(new FullDataQuery())).Items);
            await using var full = await export.CreateFullDataAsync(new FullDataQuery(), IdentityMode.Pseudonymous, ExportFormat.Json);
            Assert.True(full.Length > 0);
            await using var masks = await export.CreateMasksArchiveAsync(IdentityMode.Pseudonymous);
            Assert.True(masks.Length > 0);
        }
        finally
        {
            // Delete only this test's newly created GUID schema in the explicitly isolated test database.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static User Person(string id, string position) => new()
    {
        Id = id, Email = $"{id}@example.test", UserName = $"{id}@example.test", FullName = id, Position = position,
        Speciality = "cardiology", BodyRegion = "thorax", ImageModality = "ct", CreatedAt = DateTime.UtcNow
    };
}
