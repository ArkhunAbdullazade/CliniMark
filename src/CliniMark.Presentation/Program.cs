using System.Reflection;
using System.Net;
using System.Security.Cryptography;
using CliniMark.Core.Models;
using CliniMark.Core.Repositories;
using CliniMark.Core.Services;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Repositories;
using CliniMark.Infrastructure.Services;
using CliniMark.Infrastructure.Services.Admin;
using CliniMark.Infrastructure.Settings;
using CliniMark.Presentation.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

const string SeedDataFileName = "mockPMCMIDdata7.xlsx";
const string ResetPasswordCommand = "reset-password";
const string CreateAdminCommand = "create-admin";
const string CheckTermsCommand = "check-terms";
const string CreateParticipantsCommand = "create-participants";
const string BackfillMasksCommand = "backfill-masks";

// Operator-only shell commands, never HTTP endpoints: `dotnet CliniMark.Presentation.dll reset-password|create-admin <email>`,
// `check-terms`, `backfill-masks`, or `create-participants <participants.csv> <credentials.csv>` (Models/ParticipantImport.cs).
var operatorEmail = args is [ResetPasswordCommand or CreateAdminCommand, var email] ? email : null;
if (args.Length > 0 && args[0] is ResetPasswordCommand or CreateAdminCommand && operatorEmail is null)
{
    Console.Error.WriteLine($"Usage: {ResetPasswordCommand}|{CreateAdminCommand} <account-email>");
    Environment.ExitCode = 2;
    return;
}
var isCheckTerms = args is [CheckTermsCommand];
var isBackfillMasks = args is [BackfillMasksCommand];
var participantFiles = args is [CreateParticipantsCommand, var participantInput, var credentialsOutput]
    ? (Input: participantInput, Output: credentialsOutput) : ((string Input, string Output)?)null;
if (args.Length > 0 && args[0] == CreateParticipantsCommand && participantFiles is null)
{
    Console.Error.WriteLine($"Usage: {CreateParticipantsCommand} <participants.csv> <credentials.csv>");
    Environment.ExitCode = 2;
    return;
}
var isOperatorCommand = operatorEmail is not null || isCheckTerms || isBackfillMasks || participantFiles is not null;

var builder = WebApplication.CreateBuilder(isOperatorCommand ? [] : args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
if (builder.Environment.IsDevelopment())
{
    // Keep workstation database credentials outside tracked configuration files.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true)
        .AddEnvironmentVariables().AddCommandLine(args);
}

var medDataConnectionString = builder.Configuration.GetConnectionString("MedDataDb");
if (string.IsNullOrWhiteSpace(medDataConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:MedDataDb must be configured before the application starts.");
}

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!builder.Environment.IsDevelopment() && !isOperatorCommand && string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    throw new InvalidOperationException("Configure persistent DataProtection:KeysPath before deploying.");
}
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    var keyDirectory = Directory.CreateDirectory(dataProtectionKeysPath);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(keyDirectory);
}

builder.Services.AddDistributedMemoryCache(); // One web instance; the session only carries one-off page messages.
// Clinicians can take as long as they need: neither the session nor the sign-in runs out while the browser stays open.
// Both cookies still end when the browser is closed, so a shared hospital computer does not stay signed in.
var noTimeLimit = TimeSpan.FromDays(3650);
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = noTimeLimit;
});

builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddSessionStateTempDataProvider();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustPlatformProxy"))
    {
        // Azure App Service / Container Apps: the platform front end is the only ingress and has no fixed address.
        // ForwardLimit = 1 still reads only the client address appended by that front end.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return;
    }
    foreach (var address in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(address));
});
var accessAttemptLimit = builder.Configuration.GetValue(SignInRateLimit.LimitSetting, SignInRateLimit.DefaultPermitLimit);
if (accessAttemptLimit < 1)
{
    throw new InvalidOperationException($"{SignInRateLimit.LimitSetting} must be at least 1.");
}
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(SignInRateLimit.PolicyName, context => SignInRateLimit.Partition(context, accessAttemptLimit));
    options.AddPolicy(ProblemReportRateLimit.PolicyName, ProblemReportRateLimit.Partition);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/MedData"))
            await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message = "Too many problem reports in a short time. Wait a few minutes and try again." }, token);
        else
            await context.HttpContext.Response.WriteAsync("Too many access attempts. Wait ten minutes and try again.", token);
    };
});

builder.Services.AddDbContext<MedDataDbContext>(options =>
{
    options.UseNpgsql(
        medDataConnectionString,
        useSqlOptions => useSqlOptions.MigrationsAssembly(Assembly.GetExecutingAssembly().GetName().Name));
});

builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.User.RequireUniqueEmail = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
    .AddEntityFrameworkStores<MedDataDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Login";
    options.AccessDeniedPath = "/Identity/AccessDenied";
    options.LogoutPath = "/Identity/Logout";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.Name = "CliniMarkAuth";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = noTimeLimit;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/MedData"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new { success = false, message = "Your session has ended. Sign in again before retrying the save." });
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/MedData"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { success = false, message = "Your account is not allowed to perform this action." });
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));

builder.Services.AddTransient<IEmailService, EmailService>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IMedDataRepository, MedDataRepository>();
builder.Services.AddScoped<IAnnotatedMedDataRepository, AnnotatedMedDataRepository>();
builder.Services.AddScoped<IAnnotatedByStudentsMedDataRepository, AnnotatedByStudentsMedDataRepository>();
builder.Services.AddScoped<IExcelLoaderService, ExcelLoaderService>();
builder.Services.AddMemoryCache();
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
builder.Services.AddScoped<AdminQueryService>();
builder.Services.AddScoped<AssignmentAdminService>();
builder.Services.AddScoped<ProblemAdminService>();
builder.Services.AddScoped<ParticipantAdminService>();
builder.Services.AddScoped<ResultsExportService>();
// No overall timeout: the service limits each attempt itself.
builder.Services.AddHttpClient<ImageArchiveService>(client => client.Timeout = Timeout.InfiniteTimeSpan);
if (!isOperatorCommand) builder.Services.AddHostedService<LockSweeper>();
// Only CSS and JavaScript: HTML holds antiforgery tokens, which must never be compressed over HTTPS (BREACH).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ["text/css", "text/javascript", "application/javascript"];
});

var app = builder.Build();

if (isCheckTerms)
{
    // Read-only, and before database initialization, like the commands below.
    using var scope = app.Services.CreateScope();
    var images = await scope.ServiceProvider.GetRequiredService<MedDataDbContext>().MedDatas.AsNoTracking()
        .Where(image => image.ExcludedAt == null)
        .Select(image => new { image.Id, image.ImageDescription, Terms = image.MedDataKeywords!.OrderBy(keyword => keyword.Id).Select(keyword => keyword.Keyword).ToList() })
        .OrderBy(image => image.Id).ToListAsync();
    var problems = 0;
    foreach (var image in images)
    {
        var terms = image.Terms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term!.Trim()).Distinct().ToList<string?>();
        var unmatched = DescriptionKeywords.UnmatchedTerms(image.ImageDescription, terms);
        if (unmatched.Count == 0) continue;
        problems++;
        Console.WriteLine($"Image {image.Id}: {string.Join(" | ", unmatched)}");
    }
    Console.WriteLine($"{problems} of {images.Count} images have terms specialists could not answer, so those images would block whoever receives them.");
    Environment.ExitCode = problems == 0 ? 0 : 1;
    return;
}

if (isBackfillMasks)
{
    // Fills only masks that are empty, and runs before database initialization, like the commands below.
    using var scope = app.Services.CreateScope();
    Console.WriteLine($"{await MaskBackfill.RunAsync(scope.ServiceProvider.GetRequiredService<MedDataDbContext>())} annotations given a pixel mask.");
    return;
}

if (participantFiles is var (participantsCsv, credentialsCsv))
{
    // Runs before database initialization, like the commands below.
    using var scope = app.Services.CreateScope();
    await EnsureRolesAsync(scope.ServiceProvider);
    Environment.ExitCode = await ParticipantImport.RunAsync(participantsCsv, credentialsCsv,
        scope.ServiceProvider.GetRequiredService<IIdentityService>(), Console.Out);
    return;
}

if (isOperatorCommand)
{
    // Runs before database initialization, so it can never migrate or seed the target database.
    using var scope = app.Services.CreateScope();
    var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();
    var isCreateAdmin = args[0] == CreateAdminCommand;
    if (isCreateAdmin)
        await EnsureRolesAsync(scope.ServiceProvider);
    var (succeeded, temporaryPassword, errors) = isCreateAdmin
        ? await identityService.CreateAdminAsync(operatorEmail)
        : await identityService.ResetPasswordAsync(operatorEmail);
    Console.WriteLine(!succeeded
        ? string.Join(Environment.NewLine, errors ?? [])
        : isCreateAdmin
            ? $"Admin account created. Password: {temporaryPassword}{Environment.NewLine}Sign in as usual; the panel is at /Admin."
            : $"Temporary password: {temporaryPassword}{Environment.NewLine}Share it privately. Saved work and assignments are unchanged.");
    Environment.ExitCode = succeeded ? 0 : 1;
    return;
}

await InitializeDatabaseAsync(app);

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Platform health probes (e.g. Azure App Service) may use plain HTTP and accept only 2xx responses.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    context.Items[SecurityHeaders.NonceKey] = nonce;
    context.Response.Headers.ContentSecurityPolicy = SecurityHeaders.Policy(nonce);
    await next();
});
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // asp-append-version gives changed files a new ?v= address, so those can be cached for good.
    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl =
        file.Context.Request.Query.ContainsKey("v") ? "public,max-age=31536000,immutable" : "public,max-age=86400"
});
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

    if (ShouldAllowPath(path))
    {
        await next();
        return;
    }

    var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
    if (isAuthenticated)
    {
        if (path is "/identity/login" or "/identity/signup")
        {
            var redirectPath = context.User.IsInRole(IdentityService.AdminRole) ? "/Admin"
                : context.User.IsInRole("Medical_Student") ? "/Home/Student"
                : "/Home/Professional";

            context.Response.Redirect(redirectPath);
            return;
        }

        await next();
        return;
    }

    if (IsLoginOrSignupPath(path))
    {
        await next();
        return;
    }

    context.Response.Redirect("/Identity/Login");
});

app.MapControllerRoute(
    name: "admin",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Identity}/{action=Login}/{id?}");
app.MapGet("/health/ready", async (MedDataDbContext context, CancellationToken cancellationToken) =>
    await context.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

app.Run();

async Task InitializeDatabaseAsync(WebApplication application)
{
    using var scope = application.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        var context = services.GetRequiredService<MedDataDbContext>();
        var applyMigrations = application.Configuration.GetValue<bool?>("Database:ApplyMigrations")
            ?? application.Environment.IsDevelopment();
        if (applyMigrations) await context.Database.MigrateAsync();
        else if ((await context.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Database migrations are pending. Back up the database and apply the reviewed migration script before starting production.");

        await EnsureRolesAsync(services);

        if (!application.Configuration.GetValue<bool>("Database:SeedExcel") || await context.MedDatas.AnyAsync())
        {
            return;
        }

        var excelLoader = services.GetRequiredService<IExcelLoaderService>();
        var seedDataPath = Path.Combine(application.Environment.ContentRootPath, SeedDataFileName);
        if (!File.Exists(seedDataPath))
        {
            logger.LogWarning("Skipping seed data load because {SeedDataPath} was not found.", seedDataPath);
            return;
        }

        var medDataList = excelLoader.LoadMedDataFromExcel(seedDataPath);
        var medKeywordList = excelLoader.LoadMedKeywordsFromExcel(seedDataPath);

        await context.MedDatas.AddRangeAsync(medDataList);
        await context.SaveChangesAsync();

        var medDataKeywords = medKeywordList.Select(item => new MedDataKeyword
        {
            MedDataId = item.MedDataId,
            Keyword = item.Keyword
        });

        await context.MedDataKeywords.AddRangeAsync(medDataKeywords);
        await context.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration or seed data loading.");
        throw;
    }
}

static async Task EnsureRolesAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = ["Medical_Student", "Professional", IdentityService.AdminRole];

    foreach (var roleName in roles)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            continue;
        }

        var result = await roleManager.CreateAsync(new IdentityRole(roleName));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not initialize role {roleName}.");
        }
    }
}

static bool ShouldAllowPath(string path)
{
    if (string.IsNullOrEmpty(path))
    {
        return false;
    }

    if (path.StartsWith("/lib/") ||
        path.StartsWith("/css/") ||
        path.StartsWith("/js/") ||
        path.StartsWith("/images/") ||
        path.StartsWith("/favicon.ico"))
    {
        return true;
    }

    string[] allowedPaths =
    [
        "/identity/accessdenied",
        "/health/ready",
        "/home/error"
    ];

    return allowedPaths.Contains(path);
}

static bool IsLoginOrSignupPath(string path)
{
    if (string.IsNullOrEmpty(path))
    {
        return false;
    }

    string[] loginOrSignupPaths =
    [
        "/identity/login",
        "/identity/postlogin",
        "/identity/signup",
        "/identity/postsignup"
    ];

    return loginOrSignupPaths.Contains(path);
}
