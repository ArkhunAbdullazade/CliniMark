using MedAnnotateApp.Infrastructure.Services;
using MedAnnotateApp.Presentation.Controllers;
using MedAnnotateApp.Presentation.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedAnnotateApp.Tests;

public class ResearchGateTests
{
    [Fact]
    public async Task DirectLoginAndSignupRequestsCannotBypassSharedAccessGate()
    {
        var controller = Controller(new TestSession());
        foreach (var action in new IActionResult[]
        {
            controller.Login(), controller.Signup(),
            await controller.PostLogin(new LoginDto { Email = "person@gmail.com", Password = "AccountPassword1!" }),
            await controller.PostSignup(new SignupDto { Email = "person@gmail.com", Password = "AccountPassword1!" })
        })
        {
            Assert.Equal(nameof(IdentityController.AuthorizationAccess), Assert.IsType<RedirectToActionResult>(action).ActionName);
        }
    }

    [Fact]
    public void GateUsesConfiguredHashAndStoresOnlyAccessState()
    {
        var session = new TestSession();
        var controller = Controller(session, AuthorizationAccessPasswordService.HashPassword("TestSharedAccess1!"));

        Assert.IsType<ViewResult>(controller.PostAuthorizationAccess(new AuthorizationAccessDto { Password = "wrong" }));
        Assert.Empty(session.Keys);
        controller.ModelState.Clear();
        var result = controller.PostAuthorizationAccess(new AuthorizationAccessDto { Password = "TestSharedAccess1!" });

        Assert.Equal(nameof(IdentityController.Login), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("true", session.GetString("Authorized"));
        Assert.Equal(["Authorized"], session.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-hash")]
    public void MissingOrInvalidGateConfigurationNeverGrantsAccess(string? hash)
    {
        var session = new TestSession();
        var controller = Controller(session, hash);
        Assert.IsType<ViewResult>(controller.PostAuthorizationAccess(new AuthorizationAccessDto { Password = "TestSharedAccess1!" }));
        Assert.Empty(session.Keys);
    }

    [Fact]
    public void RepeatedCorrectGateEntriesStayValidAndWrongOrChangedPasswordsStillFail()
    {
        var hash = AuthorizationAccessPasswordService.HashPassword("GroupAccess1!");
        Assert.True(AuthorizationAccessPasswordService.VerifyPassword("GroupAccess1!", hash));
        Assert.True(AuthorizationAccessPasswordService.VerifyPassword("GroupAccess1!", hash));
        Assert.False(AuthorizationAccessPasswordService.VerifyPassword("GroupAccess1?", hash));
        Assert.False(AuthorizationAccessPasswordService.VerifyPassword("", hash));

        // A rotated gate password must not accept the previously verified one.
        var rotated = AuthorizationAccessPasswordService.HashPassword("RotatedAccess2!");
        Assert.False(AuthorizationAccessPasswordService.VerifyPassword("GroupAccess1!", rotated));
        Assert.True(AuthorizationAccessPasswordService.VerifyPassword("RotatedAccess2!", rotated));
    }

    [Fact]
    public async Task InvalidSignupDoesNotCachePersonalPasswordsInSession()
    {
        var session = new TestSession();
        session.SetString("Authorized", "true");
        var controller = Controller(session);
        controller.ModelState.AddModelError(nameof(SignupDto.BodyRegion), "Select at least one body region.");

        var result = await controller.PostSignup(new SignupDto
        {
            Email = "person@gmail.com", Password = "PersonalPassword1!", ConfirmPassword = "PersonalPassword1!"
        });

        Assert.Equal("Signup", Assert.IsType<ViewResult>(result).ViewName);
        Assert.Equal(["Authorized"], session.Keys);
    }

    [Fact]
    public async Task SignupIsClosedWhenTheStudyTeamCreatesAccounts()
    {
        var session = new TestSession();
        session.SetString("Authorized", "true");
        var controller = Controller(session, registrationEnabled: false);

        foreach (var action in new IActionResult[]
        {
            controller.Signup(),
            await controller.PostSignup(new SignupDto { Email = "person@gmail.com", Password = "AccountPassword1!" })
        })
        {
            Assert.Equal(nameof(IdentityController.Login), Assert.IsType<RedirectToActionResult>(action).ActionName);
        }
        Assert.Equal(IdentityController.SignupClosedNotice, controller.TempData["Notice"]);
    }

    [Fact]
    public void SignupStaysOpenByDefault()
    {
        var session = new TestSession();
        session.SetString("Authorized", "true");
        Assert.IsType<ViewResult>(Controller(session).Signup());
    }

    private static IdentityController Controller(TestSession session, string? hash = null, bool? registrationEnabled = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthorizationAccessPasswordHash"] = hash,
            [IdentityController.RegistrationSetting] = registrationEnabled?.ToString()
        }).Build();
        var httpContext = new DefaultHttpContext { Session = session };
        return new IdentityController(null!, null!, null!, configuration, NullLogger<IdentityController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider())
        };
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => values;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => this.values = values;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> data = [];
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => data.Keys;
        public void Clear() => data.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => data.Remove(key);
        public void Set(string key, byte[] value) => data[key] = value;
        public bool TryGetValue(string key, out byte[] value) => data.TryGetValue(key, out value!);
    }
}
