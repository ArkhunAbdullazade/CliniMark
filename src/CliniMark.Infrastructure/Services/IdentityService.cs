using System.Security.Cryptography;
using CliniMark.Core.Models;
using CliniMark.Core.Services;
using Microsoft.AspNetCore.Identity;

namespace CliniMark.Infrastructure.Services;

public class IdentityService : IIdentityService
{
    public const string AdminRole = "Admin";

    private readonly UserManager<User> userManager;
    private readonly SignInManager<User> signInManager;

    public IdentityService(UserManager<User> userManager, SignInManager<User> signInManager)
    {
        this.userManager = userManager;
        this.signInManager = signInManager;
    }

    public async Task<(bool Succeeded, IEnumerable<string>? Errors)> SignupAsync(User user, string? password, string roleName)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, ["Password is required."]);
        }

        if (roleName is not ("Professional" or "Medical_Student"))
        {
            return (false, ["Choose a valid research participant position."]);
        }

        user.CreatedAt = DateTime.UtcNow;
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return (false, result.Errors.Select(error => error.Description).ToArray());
        }

        var roleResult = await userManager.AddToRoleAsync(user, roleName);
        if (roleResult.Succeeded)
        {
            return (true, null);
        }

        // Remove only the just-created account if assigning its research role fails.
        var cleanup = await userManager.DeleteAsync(user);
        return (false, [cleanup.Succeeded
            ? "Your account could not be created. Please try again or contact the researcher."
            : "Your account needs attention before you can sign in. Please contact the researcher."]);
    }

    public async Task<(bool Succeeded, IEnumerable<string>? Errors)> LoginAsync(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, ["Email address is required."]);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, ["Password is required."]);
        }

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user == null)
        {
            return (false, ["Email or password is incorrect. Please try again."]);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return (true, null);
        }

        if (result.IsLockedOut)
        {
            return (false, ["This account has been locked due to too many failed login attempts. Please try again later."]);
        }

        if (result.IsNotAllowed)
        {
            return (false, ["This account is not allowed to login. Please contact support."]);
        }

        if (result.RequiresTwoFactor)
        {
            return (false, ["Two factor authentication is required but is not supported by this application."]);
        }

        return (false, ["Email or password is incorrect. Please try again."]);
    }

    public async Task SignoutAsync()
    {
        await signInManager.SignOutAsync();
    }

    // Operator-only recovery (no email is configured). Keeps the account id, so saved work and assignments stay with it.
    public async Task<(bool Succeeded, string? TemporaryPassword, IEnumerable<string>? Errors)> ResetPasswordAsync(string? email)
    {
        var user = string.IsNullOrWhiteSpace(email) ? null : await userManager.FindByEmailAsync(email.Trim());
        if (user == null)
        {
            return (false, null, ["No account uses that email address."]);
        }

        var temporaryPassword = NewTemporaryPassword();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!result.Succeeded)
        {
            return (false, null, result.Errors.Select(error => error.Description).ToArray());
        }

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return (true, temporaryPassword, null);
    }

    // Operator-only: admin accounts never come from signup and never hold an annotator role, so they cannot add study data.
    public async Task<(bool Succeeded, string? TemporaryPassword, IEnumerable<string>? Errors)> CreateAdminAsync(string? email)
    {
        email = email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return (false, null, ["Give the admin's email address."]);
        }

        if (await userManager.FindByEmailAsync(email) != null)
        {
            return (false, null, ["An account already uses that email address. Admin accounts need their own address."]);
        }

        var temporaryPassword = NewTemporaryPassword();
        var admin = new User { Email = email, UserName = email, FullName = "Study administrator", CreatedAt = DateTime.UtcNow };
        var result = await userManager.CreateAsync(admin, temporaryPassword);
        if (!result.Succeeded)
        {
            return (false, null, result.Errors.Select(error => error.Description).ToArray());
        }

        var roleResult = await userManager.AddToRoleAsync(admin, AdminRole);
        if (roleResult.Succeeded)
        {
            return (true, temporaryPassword, null);
        }

        await userManager.DeleteAsync(admin);
        return (false, null, roleResult.Errors.Select(error => error.Description).ToArray());
    }

    // Study-team-created participant account (operator command create-participants): same checks and role rules as
    // signup, with a generated password. An existing account is never changed.
    public async Task<(bool Succeeded, string? TemporaryPassword, IEnumerable<string>? Errors)> CreateParticipantAsync(User user, string roleName)
    {
        if (string.IsNullOrWhiteSpace(user.Email) || await userManager.FindByEmailAsync(user.Email) != null)
        {
            return (false, null, ["An account already uses this email address; it was left unchanged."]);
        }

        var password = NewTemporaryPassword();
        var (succeeded, errors) = await SignupAsync(user, password, roleName);
        return succeeded ? (true, password, null) : (false, null, errors);
    }

    // Random letters and digits plus a fixed mixed suffix always satisfy the Identity password policy.
    private static string NewTemporaryPassword() =>
        RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 16) + "-7a";
}
