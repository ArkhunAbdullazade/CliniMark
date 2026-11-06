using CliniMark.Core.Models;
using CliniMark.Core.Services;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services.Admin;

public sealed class ParticipantAdminService
{
    private readonly MedDataDbContext context;
    private readonly UserManager<User> userManager;

    public ParticipantAdminService(MedDataDbContext context, UserManager<User> userManager)
    {
        this.context = context;
        this.userManager = userManager;
    }

    /// <summary>
    /// Corrects a mistyped email. Resuming work, completion and exports all match on the address copied onto each saved
    /// row, so the account and its rows change in one transaction, under the workflow lock so no save can slip between.
    /// Identity changes the security stamp too, which signs the person out within about 30 minutes.
    /// </summary>
    public async Task<ReleaseResult> CorrectEmailAsync(string userId, string? newEmail, string adminUserId, CancellationToken cancellationToken = default)
    {
        newEmail = newEmail?.Trim();
        if (!EmailFormat.IsValid(newEmail))
            return new ReleaseResult(false, "Enter a complete email address, such as name@example.org.");

        await using var transaction = await MedDataRepository.BeginWorkflowAsync(context, cancellationToken);
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return new ReleaseResult(false, "This participant no longer exists.");
        if (await userManager.IsInRoleAsync(user, IdentityService.AdminRole))
            return new ReleaseResult(false, "Admin accounts can't be changed here.");
        var oldEmail = user.Email;
        if (oldEmail == newEmail) return new ReleaseResult(false, "That is already this participant's email address.");
        var other = await userManager.FindByEmailAsync(newEmail!);
        if (other != null && other.Id != user.Id)
            return new ReleaseResult(false, "Another account already uses that address, so nothing was changed. That person can keep signing in with that account.");

        var result = await userManager.SetUserNameAsync(user, newEmail);
        if (result.Succeeded) result = await userManager.SetEmailAsync(user, newEmail);
        if (!result.Succeeded)
            return new ReleaseResult(false, string.Join(" ", result.Errors.Select(error => error.Description)));

        var answers = await context.AnnotatedMedDatas.Where(item => item.Email == oldEmail)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Email, newEmail), cancellationToken);
        var groups = await context.AnnotatedByStudentsMedDatas.Where(item => item.Email == oldEmail)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Email, newEmail), cancellationToken);
        // Counts only: the admin log never holds an address.
        context.AdminActions.Add(AssignmentAdminService.Audit(adminUserId, "correct-email", null, user.Id, new { answers, groups }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReleaseResult(true, $"Email corrected. {answers + groups} saved row(s) moved with it. They now sign in with the new address and their existing password.");
    }
}
