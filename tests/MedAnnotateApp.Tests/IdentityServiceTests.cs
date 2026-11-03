using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Infrastructure.Data;
using MedAnnotateApp.Infrastructure.Repositories;
using MedAnnotateApp.Infrastructure.Services;
using MedAnnotateApp.Infrastructure.Services.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedAnnotateApp.Tests;

public class IdentityServiceTests
{
    [Fact]
    public async Task OrdinaryEmailSignupHashesPasswordAndCanSignInWithoutEmailConfirmation()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "specialist@gmail.com", UserName = "specialist@gmail.com" };
        var signup = await fixture.Identity.SignupAsync(user, "PersonalPassword1!", "Professional");

        Assert.True(signup.Succeeded);
        Assert.False(user.EmailConfirmed);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual("PersonalPassword1!", user.PasswordHash);
        Assert.True(await fixture.Users.CheckPasswordAsync(user, "PersonalPassword1!"));
        Assert.True(await fixture.Users.IsInRoleAsync(user, "Professional"));

        var login = await fixture.Identity.LoginAsync(user.Email, "PersonalPassword1!");
        Assert.True(login.Succeeded);
        Assert.False(user.EmailConfirmed);
        Assert.True(fixture.Context.Response.Headers.SetCookie.Count > 0);
    }

    [Fact]
    public async Task ExistingUnconfirmedAccountKeepsPasswordAndRemainsUnconfirmedAfterLogin()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "existing@outlook.com", UserName = "existing@outlook.com", EmailConfirmed = false };
        Assert.True((await fixture.Users.CreateAsync(user, "ExistingPassword1!")).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "Professional")).Succeeded);
        var passwordHash = user.PasswordHash;

        var login = await fixture.Identity.LoginAsync(user.Email, "ExistingPassword1!");

        Assert.True(login.Succeeded);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(passwordHash, user.PasswordHash);
    }

    [Fact]
    public async Task IncorrectPasswordsAndUnknownEmailsHaveSameErrorAndFailuresCountTowardLockout()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "specialist@gmail.com", UserName = "specialist@gmail.com" };
        Assert.True((await fixture.Users.CreateAsync(user, "PersonalPassword1!")).Succeeded);

        var wrongPassword = await fixture.Identity.LoginAsync(user.Email, "WrongPassword1!");
        var unknownEmail = await fixture.Identity.LoginAsync("nobody@gmail.com", "WrongPassword1!");
        Assert.False(wrongPassword.Succeeded);
        Assert.Equal(unknownEmail.Errors, wrongPassword.Errors);
        Assert.Equal(1, await fixture.Users.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task SignupCannotChooseAnAdministrativeRole()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "someone@gmail.com", UserName = "someone@gmail.com" };

        Assert.False((await fixture.Identity.SignupAsync(user, "PersonalPassword1!", "Administrator")).Succeeded);
        Assert.Null(await fixture.Users.FindByEmailAsync(user.Email));
    }

    [Fact]
    public async Task OperatorResetKeepsAccountIdentityClearsLockoutAndReplacesOnlyThePassword()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "forgetful@gmail.com", UserName = "forgetful@gmail.com" };
        Assert.True((await fixture.Users.CreateAsync(user, "ForgottenPassword1!")).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "Professional")).Succeeded);
        var userId = user.Id;
        await fixture.Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));

        var reset = await fixture.Identity.ResetPasswordAsync(" forgetful@gmail.com ");

        Assert.True(reset.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(reset.TemporaryPassword));
        Assert.Equal(userId, user.Id);
        Assert.False(await fixture.Users.CheckPasswordAsync(user, "ForgottenPassword1!"));
        Assert.True(await fixture.Users.CheckPasswordAsync(user, reset.TemporaryPassword!));
        Assert.False(await fixture.Users.IsLockedOutAsync(user));
        Assert.True(await fixture.Users.IsInRoleAsync(user, "Professional"));
        Assert.True((await fixture.Identity.LoginAsync(user.Email, reset.TemporaryPassword)).Succeeded);
    }

    [Fact]
    public async Task OperatorResetRejectsUnknownAndBlankEmails()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        Assert.False((await fixture.Identity.ResetPasswordAsync("nobody@gmail.com")).Succeeded);
        Assert.False((await fixture.Identity.ResetPasswordAsync(" ")).Succeeded);
    }

    [Fact]
    public async Task LoginIgnoresSpacesAroundTheEmail()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "specialist@gmail.com", UserName = "specialist@gmail.com" };
        Assert.True((await fixture.Users.CreateAsync(user, "PersonalPassword1!")).Succeeded);

        Assert.True((await fixture.Identity.LoginAsync(" specialist@gmail.com ", "PersonalPassword1!")).Succeeded);
    }

    [Fact]
    public async Task EmailCorrectionMovesSavedWorkAndSwitchesTheLogin()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "typo@gmial.com", UserName = "typo@gmial.com", FullName = "Dr Typo", Speciality = "cardiology", BodyRegion = "thorax", ImageModality = "ct" };
        Assert.True((await fixture.Users.CreateAsync(user, "PersonalPassword1!")).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "Professional")).Succeeded);
        var other = new User { Email = "other@gmail.com", UserName = "other@gmail.com" };
        Assert.True((await fixture.Users.CreateAsync(other, "PersonalPassword1!")).Succeeded);
        var stamp = user.SecurityStamp;
        var db = fixture.Database;
        db.MedDatas.AddRange(new MedData { Id = 1, ImageUrl = "https://images.example.test/1.png", Speciality = "cardiology", BodyRegion = "thorax", Modality = "ct" });
        db.MedDataKeywords.AddRange(new MedDataKeyword { MedDataId = 1, Keyword = "lesion" }, new MedDataKeyword { MedDataId = 1, Keyword = "margin" });
        db.AnnotatedMedDatas.AddRange(
            new AnnotatedMedData { MedDataId = 1, Email = user.Email, ExtractedKeyword = "lesion", PressedButton = "Uncertain/Skip" },
            new AnnotatedMedData { MedDataId = 1, Email = other.Email, ExtractedKeyword = "lesion", PressedButton = "Uncertain/Skip" });
        db.AnnotatedByStudentsMedDatas.Add(new AnnotatedByStudentsMedData { MedDataId = 1, Email = user.Email, TextualAnnotation = "lesion" });
        await db.SaveChangesAsync();
        var repository = new MedDataRepository(db);
        Assert.Equal(1, (await repository.GetNthMedDataBySpecialityAndPositionAsync(user.Speciality, "professional", user.BodyRegion, user.ImageModality, user.Id)).MedData?.Id);
        var service = new ParticipantAdminService(db, fixture.Users);

        var result = await service.CorrectEmailAsync(user.Id, " right@gmail.com ", "admin-id");

        Assert.True(result.Released);
        db.ChangeTracker.Clear();
        Assert.Equal(["other@gmail.com", "right@gmail.com"], (await db.AnnotatedMedDatas.AsNoTracking().Select(item => item.Email!).ToListAsync()).Order());
        Assert.Equal("right@gmail.com", (await db.AnnotatedByStudentsMedDatas.AsNoTracking().SingleAsync()).Email);
        var changed = await fixture.Users.FindByEmailAsync("right@gmail.com");
        Assert.Equal(user.Id, changed!.Id);
        Assert.Equal("right@gmail.com", changed.UserName);
        Assert.NotEqual(stamp, changed.SecurityStamp);
        Assert.Null(await fixture.Users.FindByEmailAsync("typo@gmial.com"));
        Assert.False((await fixture.Identity.LoginAsync("typo@gmial.com", "PersonalPassword1!")).Succeeded);
        Assert.True((await fixture.Identity.LoginAsync("right@gmail.com", "PersonalPassword1!")).Succeeded);

        // Their partial work follows them: the saved term still counts and the next term is still open.
        var saved = await repository.GetProfessionalAnnotationsAsync(1, changed);
        Assert.Equal("lesion", Assert.Single(saved!).ExtractedKeyword);
        Assert.True((await repository.SaveProfessionalAsync(changed, new AnnotatedMedData { MedDataId = 1, ExtractedKeyword = "margin", PressedButton = "Uncertain/Skip" })).Success);
        Assert.True((await repository.CompleteProfessionalAsync(1, changed)).Success);

        var audit = await db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(("correct-email", user.Id, "admin-id"), (audit.Action, audit.TargetUserId, audit.AdminUserId));
        Assert.DoesNotContain("@", audit.Details);
    }

    [Theory]
    [InlineData("other@gmail.com")]
    [InlineData("OTHER@gmail.com")]
    [InlineData("not-an-address")]
    [InlineData("name@gmail")]
    [InlineData("")]
    [InlineData("typo@gmial.com")]
    public async Task EmailCorrectionRefusesBadOrTakenAddressesAndChangesNothing(string email)
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "typo@gmial.com", UserName = "typo@gmial.com" };
        Assert.True((await fixture.Users.CreateAsync(user, "PersonalPassword1!")).Succeeded);
        Assert.True((await fixture.Users.CreateAsync(new User { Email = "other@gmail.com", UserName = "other@gmail.com" }, "PersonalPassword1!")).Succeeded);
        var db = fixture.Database;
        db.MedDatas.Add(new MedData { Id = 1 });
        db.AnnotatedMedDatas.Add(new AnnotatedMedData { MedDataId = 1, Email = user.Email, ExtractedKeyword = "lesion" });
        await db.SaveChangesAsync();

        var result = await new ParticipantAdminService(db, fixture.Users).CorrectEmailAsync(user.Id, email, "admin-id");

        Assert.False(result.Released);
        db.ChangeTracker.Clear();
        Assert.Equal("typo@gmial.com", (await db.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id)).Email);
        Assert.Equal("typo@gmial.com", (await db.AnnotatedMedDatas.AsNoTracking().SingleAsync()).Email);
        Assert.Empty(await db.AdminActions.ToListAsync());
    }

    [Fact]
    public async Task EmailCorrectionRefusesAdminAccounts()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var admin = await fixture.Identity.CreateAdminAsync("lead@gmail.com");
        var account = await fixture.Users.FindByEmailAsync("lead@gmail.com");

        var result = await new ParticipantAdminService(fixture.Database, fixture.Users).CorrectEmailAsync(account!.Id, "new@gmail.com", "admin-id");

        Assert.True(admin.Succeeded);
        Assert.False(result.Released);
        Assert.NotNull(await fixture.Users.FindByEmailAsync("lead@gmail.com"));
    }

    [Fact]
    public async Task CreateAdminMakesAnAdminOnlyAccountAndNeverReusesAnAnnotatorEmail()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var annotator = new User { Email = "specialist@gmail.com", UserName = "specialist@gmail.com" };
        Assert.True((await fixture.Identity.SignupAsync(annotator, "PersonalPassword1!", "Professional")).Succeeded);
        Assert.NotNull(annotator.CreatedAt);

        var created = await fixture.Identity.CreateAdminAsync(" lead@study.test ");
        var duplicate = await fixture.Identity.CreateAdminAsync("specialist@gmail.com");

        Assert.True(created.Succeeded);
        var admin = await fixture.Users.FindByEmailAsync("lead@study.test");
        Assert.NotNull(admin);
        Assert.Equal(["Admin"], await fixture.Users.GetRolesAsync(admin));
        Assert.True(await fixture.Users.CheckPasswordAsync(admin, created.TemporaryPassword!));
        Assert.False(duplicate.Succeeded);
        Assert.Equal(["Professional"], await fixture.Users.GetRolesAsync(annotator));
        Assert.False((await fixture.Identity.CreateAdminAsync("not-an-email")).Succeeded);
    }

    [Fact]
    public async Task SignupCannotGrantTheAdminRole()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = new User { Email = "person@gmail.com", UserName = "person@gmail.com" };

        Assert.False((await fixture.Identity.SignupAsync(user, "PersonalPassword1!", "Admin")).Succeeded);
        Assert.Null(await fixture.Users.FindByEmailAsync("person@gmail.com"));
    }

    [Fact]
    public async Task StudyTeamCreatesParticipantsWithGeneratedPasswordsAndNeverChangesExistingAccounts()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var participant = new User { Email = "clinician@hospital.example", UserName = "clinician@hospital.example", FullName = "Clinician",
            Position = "specialist", University = "Hospital", BodyRegion = "chest and thorax" };

        var (created, password, errors) = await fixture.Identity.CreateParticipantAsync(participant, "Professional");

        Assert.True(created, string.Join(" ", errors ?? []));
        Assert.False(string.IsNullOrWhiteSpace(password));
        var stored = await fixture.Users.FindByEmailAsync("clinician@hospital.example");
        Assert.NotNull(stored);
        Assert.True(await fixture.Users.CheckPasswordAsync(stored!, password!));
        Assert.True(await fixture.Users.IsInRoleAsync(stored!, "Professional"));
        Assert.Equal("chest and thorax", stored!.BodyRegion);
        Assert.NotNull(stored.CreatedAt);

        var again = new User { Email = "clinician@hospital.example", UserName = "clinician@hospital.example", FullName = "Someone else" };
        var (second, secondPassword, secondErrors) = await fixture.Identity.CreateParticipantAsync(again, "Medical_Student");
        Assert.False(second);
        Assert.Null(secondPassword);
        Assert.Contains("left unchanged", Assert.Single(secondErrors!));
        stored = await fixture.Users.FindByEmailAsync("clinician@hospital.example");
        Assert.Equal("Clinician", stored!.FullName);
        Assert.True(await fixture.Users.CheckPasswordAsync(stored, password!));
        Assert.False(await fixture.Users.IsInRoleAsync(stored, "Medical_Student"));
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;
        public DefaultHttpContext Context { get; }
        public UserManager<User> Users { get; }
        public MedDataDbContext Database { get; }
        public IdentityService Identity { get; }

        private IdentityFixture(SqliteConnection connection, ServiceProvider provider, IServiceScope scope)
        {
            this.connection = connection;
            this.provider = provider;
            this.scope = scope;
            Context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = Context;
            Users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Database = scope.ServiceProvider.GetRequiredService<MedDataDbContext>();
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<User>>();
            // Pin the request on this fixture; IHttpContextAccessor uses an AsyncLocal
            // that does not flow back out of the asynchronous fixture factory.
            signIn.Context = Context;
            Identity = new IdentityService(Users, signIn);
        }

        public static async Task<IdentityFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddDbContext<MedDataDbContext>(options => options.UseSqlite(connection));
            services.AddIdentity<User, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequiredLength = 8;
            }).AddEntityFrameworkStores<MedDataDbContext>().AddDefaultTokenProviders();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MedDataDbContext>().Database.EnsureCreatedAsync();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("Professional"))).Succeeded);
            Assert.True((await roles.CreateAsync(new IdentityRole("Medical_Student"))).Succeeded);
            Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
            return new IdentityFixture(connection, provider, scope);
        }

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
