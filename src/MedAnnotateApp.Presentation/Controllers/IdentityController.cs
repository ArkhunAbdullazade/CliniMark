using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Repositories;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Infrastructure.Services;
using MedAnnotateApp.Presentation.Dtos;
using MedAnnotateApp.Presentation.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedAnnotateApp.Presentation.Controllers;

public class IdentityController : Controller
{
    private readonly IMedDataRepository medDataRepository;
    private readonly IIdentityService identityService;
    private readonly UserManager<User> userManager;
    private readonly IConfiguration configuration;
    private readonly ILogger<IdentityController> logger;

    public IdentityController(
        IMedDataRepository medDataRepository,
        IIdentityService identityService,
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger<IdentityController> logger)
    {
        this.medDataRepository = medDataRepository;
        this.identityService = identityService;
        this.userManager = userManager;
        this.configuration = configuration;
        this.logger = logger;
    }

    [AllowAnonymous]
    public IActionResult AuthorizationAccess()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToRoleBasedPage();
        }

        if (HasResearchAccess())
        {
            return RedirectToAction(nameof(Login));
        }

        ModelState.Clear();
        HandleTempDataErrors();

        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(ResearchAccessRateLimit.PolicyName)]
    public IActionResult PostAuthorizationAccess(AuthorizationAccessDto authDto)
    {
        logger.LogInformation("Authorization access attempt");

        if (!ModelState.IsValid)
        {
            logger.LogWarning(
                "Authorization access validation failed: {Errors}",
                string.Join(", ", ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage)));

            return View("AuthorizationAccess", authDto);
        }

        var hashedPassword = configuration["AuthorizationAccessPasswordHash"];
        if (AuthorizationAccessPasswordService.VerifyPassword(authDto.Password ?? string.Empty, hashedPassword))
        {
            logger.LogInformation("Authorization access granted");
            HttpContext.Session.SetString("Authorized", "true");
            return RedirectToAction(nameof(Login));
        }

        if (string.IsNullOrWhiteSpace(hashedPassword))
        {
            logger.LogError("AuthorizationAccessPasswordHash is not configured.");
        }
        else
        {
            logger.LogWarning("Authorization access denied.");
        }

        ModelState.AddModelError(string.Empty, "The research access password is incorrect. Please try again or contact the researcher.");
        return View("AuthorizationAccess", authDto);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToRoleBasedPage();
        }

        if (!HasResearchAccess()) return RedirectToAction(nameof(AuthorizationAccess));

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            TempData["ReturnUrl"] = returnUrl;
        }

        ModelState.Clear();
        HandleTempDataErrors();

        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(ResearchAccessRateLimit.PolicyName)]
    public async Task<IActionResult> PostLogin(LoginDto loginDto)
    {
        if (!HasResearchAccess()) return RedirectToAction(nameof(AuthorizationAccess));

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Login form validation failed");
            return View("Login", loginDto);
        }

        var (succeeded, errors) = await identityService.LoginAsync(loginDto.Email, loginDto.Password);
        if (succeeded)
        {
            logger.LogInformation("Login succeeded.");

            if (TempData["ReturnUrl"] is string returnUrl && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Resolve the destination through the authenticated home request.
            return RedirectToAction("Index", "Home");
        }

        logger.LogWarning(
            "Login failed: {Errors}",
            errors != null && errors.Any() ? string.Join(", ", errors) : "No specific errors returned");

        foreach (var error in errors ?? ["Invalid email or password. Please try again."])
        {
            ModelState.AddModelError(string.Empty, error);
        }

        return View("Login", loginDto);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout([FromBody] LogoutDto? logoutDto)
    {
        var userId = userManager.GetUserId(User);
        var isMedicalStudent = User.IsInRole("Medical_Student");
        try
        {
            if (logoutDto?.MedDataId is int medDataId && userId is not null)
            {
                logger.LogDebug(
                    "Releasing annotation lock for MedDataId {MedDataId}. AnnotationStarted: {AnnotationStarted}",
                    medDataId,
                    logoutDto.IsAnnotationStarted);

                await medDataRepository.UpdateLock(
                    medDataId,
                    logoutDto.KeywordStates ?? string.Empty,
                    logoutDto.IsAnnotationStarted,
                    isMedicalStudent,
                    userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during logout");
        }
        await identityService.SignoutAsync();
        HttpContext.Session.Clear();
        return Json(new { success = true });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Signup()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToRoleBasedPage();
        }

        if (!HasResearchAccess()) return RedirectToAction(nameof(AuthorizationAccess));
        HandleTempDataErrors();
        // Drop any legacy form cache: older versions stored passwords in TempData.
        TempData.Remove("FormData");
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(ResearchAccessRateLimit.PolicyName)]
    public async Task<IActionResult> PostSignup([FromForm] SignupDto signupDto)
    {
        if (!HasResearchAccess()) return RedirectToAction(nameof(AuthorizationAccess));
        try
        {

            if (!ModelState.IsValid)
            {
                return View("Signup", signupDto);
            }

            var newUser = new User
            {
                Email = signupDto.Email?.Trim(),
                FullName = signupDto.FullName?.Trim(),
                UserName = signupDto.Email?.Trim(),
                University = signupDto.University,
                Position = signupDto.Position,
                Speciality = ImageFiltersDto.JoinSelection(signupDto.Speciality),
                BodyRegion = ImageFiltersDto.JoinSelection(signupDto.BodyRegion),
                ImageModality = ImageFiltersDto.JoinSelection(signupDto.ImageModality),
                ClinicalExperience = signupDto.ClinicalExperience ?? 0,
                OrcidId = signupDto.OrcidId,
            };

            var roleName = signupDto.Position == "medical student" ? "Medical_Student" : "Professional";
            var (succeeded, signupErrors) = await identityService.SignupAsync(newUser, signupDto.Password, roleName);
            if (succeeded)
            {
                logger.LogInformation("User created successfully and assigned role: {Role}", roleName);
                TempData["Notice"] = "Your account is ready. Sign in with your email address and account password.";
                return RedirectToAction(nameof(Login));
            }

            var errorList = signupErrors?.ToList() ?? [];
            if (errorList.Count == 0)
            {
                errorList.Add("This email is already in use.");
            }

            foreach (var error in errorList) ModelState.AddModelError(string.Empty, error);
            return View("Signup", signupDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during signup process");
            ModelState.AddModelError(string.Empty, "Your account could not be created. Please try again or contact the researcher.");
            return View("Signup", signupDto);
        }
    }

    [HttpGet]
    [Authorize(Roles = "Professional,Medical_Student")]
    public async Task<IActionResult> Filters()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(new ImageFiltersDto
        {
            Speciality = SplitSelection(user.Speciality),
            BodyRegion = SplitSelection(user.BodyRegion),
            ImageModality = SplitSelection(user.ImageModality)
        });
    }

    [HttpPost]
    [Authorize(Roles = "Professional,Medical_Student")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Filters(ImageFiltersDto filters)
    {
        if (!ModelState.IsValid) return View(filters);
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var speciality = ImageFiltersDto.JoinSelection(filters.Speciality);
        var bodyRegion = ImageFiltersDto.JoinSelection(filters.BodyRegion);
        var modality = ImageFiltersDto.JoinSelection(filters.ImageModality);
        if (user.Speciality != speciality || user.BodyRegion != bodyRegion || user.ImageModality != modality)
        {
            await medDataRepository.ReleaseAssignmentsAsync(user.Id, User.IsInRole("Medical_Student"));
            user.Speciality = speciality;
            user.BodyRegion = bodyRegion;
            user.ImageModality = modality;
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Your image selections could not be saved. Please try again.");
                return View(filters);
            }
        }
        return RedirectToRoleBasedPage();
    }

    private bool HasResearchAccess() => HttpContext.Session.GetString("Authorized") == "true";

    private static string[] SplitSelection(string? value) => (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToRoleBasedPage()
    {
        if (User.IsInRole(IdentityService.AdminRole))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        if (User.IsInRole("Medical_Student"))
        {
            return RedirectToAction("Student", "Home");
        }

        if (User.IsInRole("Professional"))
        {
            return RedirectToAction("Professional", "Home");
        }

        logger.LogWarning("User {UserId} has no role assigned.", userManager.GetUserId(User));
        TempData["Errors"] = new List<string> { "Your account doesn't have any assigned roles. Please contact the administrator." };
        return RedirectToAction("AccessDenied", "Identity");
    }

    private void HandleTempDataErrors()
    {
        var errors = TempData["Errors"] switch
        {
            string error => [error],
            IEnumerable<string> errorList => errorList.ToArray(),
            _ => []
        };

        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

    }
}
