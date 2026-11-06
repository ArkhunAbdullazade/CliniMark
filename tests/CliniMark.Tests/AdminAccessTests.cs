using System.Reflection;
using CliniMark.Presentation.Areas.Admin.Controllers;
using CliniMark.Presentation.Areas.Admin.Models;
using CliniMark.Presentation.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Tests;

public class AdminAccessTests
{
    [Fact]
    public void EveryAdminControllerRequiresTheAdminRoleAndNoStore()
    {
        var controllers = typeof(AdminController).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AdminController).Namespace && typeof(Controller).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList();

        Assert.True(controllers.Count >= 7);
        Assert.All(controllers, controller => Assert.True(controller.IsSubclassOf(typeof(AdminController)), controller.Name));
        var authorize = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin", authorize?.Roles);
        Assert.Equal("Admin", typeof(AdminController).GetCustomAttribute<AreaAttribute>()?.RouteValue);
        Assert.True(typeof(AdminController).GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
        Assert.All(controllers.SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
            action => Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    [Theory]
    [InlineData(nameof(HomeController.Professional), "Professional")]
    [InlineData(nameof(HomeController.Student), "Medical_Student")]
    public void AnnotationPagesStayClosedToAdminAccounts(string action, string role) =>
        Assert.Equal(role, typeof(HomeController).GetMethod(action)?.GetCustomAttribute<AuthorizeAttribute>()?.Roles);

    [Theory]
    [InlineData("12:1:user-id", 12, true, "user-id")]
    [InlineData("7:0:a:b", 7, false, "a:b")]
    public void LockKeysRoundTrip(string key, int imageId, bool student, string holder) =>
        Assert.Equal((imageId, student, holder), LocksViewModel.ParseKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("x:1:user")]
    [InlineData("3:2:user")]
    [InlineData("3:1:")]
    public void MalformedLockKeysAreIgnored(string? key) => Assert.Null(LocksViewModel.ParseKey(key));
}
