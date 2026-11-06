using CliniMark.Presentation.Controllers;
using CliniMark.Presentation.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CliniMark.Tests;

public class SignInAccessTests
{
    [Fact]
    public void TheSignInPageOpensWithoutAnySharedPassword()
    {
        Assert.IsType<ViewResult>(Controller(new TestSession()).Login());
    }

    [Fact]
    public async Task InvalidSignupDoesNotCachePersonalPasswordsInSession()
    {
        var session = new TestSession();
        var controller = Controller(session);
        controller.ModelState.AddModelError(nameof(SignupDto.BodyRegion), "Select at least one body region.");

        var result = await controller.PostSignup(new SignupDto
        {
            Email = "person@gmail.com", Password = "PersonalPassword1!", ConfirmPassword = "PersonalPassword1!"
        });

        Assert.Equal("Signup", Assert.IsType<ViewResult>(result).ViewName);
        Assert.Empty(session.Keys);
    }

    [Fact]
    public async Task SignupIsClosedWhenTheStudyTeamCreatesAccounts()
    {
        var controller = Controller(new TestSession(), registrationEnabled: false);

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
        Assert.IsType<ViewResult>(Controller(new TestSession()).Signup());
    }

    private static IdentityController Controller(TestSession session, bool? registrationEnabled = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
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
