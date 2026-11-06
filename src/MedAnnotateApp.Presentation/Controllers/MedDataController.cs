using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Repositories;
using MedAnnotateApp.Presentation.Dtos;
using MedAnnotateApp.Presentation.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedAnnotateApp.Presentation.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MedDataController : Controller
{
    private readonly IMedDataRepository medDataRepository;
    private readonly UserManager<User> userManager;
    private readonly ILogger<MedDataController> logger;

    public MedDataController(IMedDataRepository medDataRepository, UserManager<User> userManager, ILogger<MedDataController> logger)
    {
        this.medDataRepository = medDataRepository;
        this.userManager = userManager;
        this.logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "Professional")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> ProcessAnnotatedMedData([FromBody] AnnotatedMedDataDto? dto)
    {
        if (!ModelState.IsValid || dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Invalid annotation payload." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });

        try
        {
            // Image and researcher metadata always come from the database, never the browser.
            var result = await medDataRepository.SaveProfessionalAsync(user, new AnnotatedMedData
            {
                MedDataId = dto.Id,
                BoxCoordinates = dto.BoxCoordinates,
                ExtractedKeyword = dto.ExtractedKeyword,
                Timestamps = dto.Timestamps,
                PressedButton = dto.PressedButton,
                Comment = dto.Comment,
                TechnicalProblem = dto.TechnicalProblem
            });
            return SaveResult(result);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Annotation save failed for image {MedDataId} and user {UserId}.", dto.Id, user.Id);
            return StatusCode(503, new { success = false, message = "The annotation could not be saved. Keep this page open and retry." });
        }
    }

    [HttpPut]
    [Authorize(Roles = "Professional")]
    public async Task<IActionResult> NextImage([FromQuery] int medDataId)
    {
        if (medDataId <= 0) return BadRequest(new { success = false, message = "Invalid image id." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });
        try { return SaveResult(await medDataRepository.CompleteProfessionalAsync(medDataId, user)); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Completing image {MedDataId} failed for user {UserId}.", medDataId, user.Id);
            return StatusCode(503, new { success = false, message = "Completion could not be confirmed. Keep this page open and retry." });
        }
    }

    [HttpPut]
    [Authorize(Roles = "Professional")]
    public async Task<IActionResult> SkipImage([FromQuery] int medDataId)
    {
        if (medDataId <= 0) return BadRequest(new { success = false, message = "Invalid image id." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });
        try { return SaveResult(await medDataRepository.SkipImageAsync(medDataId, user)); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Skipping image {MedDataId} failed for user {UserId}.", medDataId, user.Id);
            return StatusCode(503, new { success = false, message = "The image could not be skipped. Keep this page open and retry." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "Professional")]
    public async Task<IActionResult> CurrentAnnotations([FromQuery] int medDataId)
    {
        if (medDataId <= 0) return BadRequest(new { success = false, message = "Invalid image id." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false });
        var annotations = await medDataRepository.GetProfessionalAnnotationsAsync(medDataId, user);
        if (annotations == null) return StatusCode(403, new { success = false, message = "This image is not assigned to you." });
        var customs = await medDataRepository.GetCustomAnnotationsAsync(medDataId, user) ?? [];
        var checks = await medDataRepository.GetMetadataChecksAsync(medDataId, user) ?? [];
        return Json(new
        {
            success = true,
            annotations = annotations.Select(item => new { item.ExtractedKeyword, item.PressedButton, item.BoxCoordinates, item.Comment, item.TechnicalProblem }),
            customAnnotations = customs.Select(item => new { item.Id, item.Term, item.Coordinates }),
            metadataChecks = checks.Select(item => new { item.Field, item.Verdict, item.CorrectedValue, item.IsNewValue })
        });
    }

    [HttpPost]
    [Authorize(Roles = "Professional")]
    [RequestSizeLimit(64_000)]
    public async Task<IActionResult> SaveMetadataChecks([FromBody] MetadataCheckListDto? dto)
    {
        if (!ModelState.IsValid || dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Invalid label check." });
        if (!MetadataCheckOptions.TryRead(dto.Checks, out var answers, out var problem))
            return BadRequest(new { success = false, message = problem });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });
        try { return SaveResult(await medDataRepository.SaveMetadataChecksAsync(user, dto.Id, answers)); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Label check save failed for image {MedDataId} and user {UserId}.", dto.Id, user.Id);
            return StatusCode(503, new { success = false, message = "The answer could not be saved. Keep this page open and retry." });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Professional")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> SaveCustomAnnotation([FromBody] CustomAnnotationDto? dto)
    {
        if (!ModelState.IsValid || dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Name the annotation and draw a closed contour before saving." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });
        try
        {
            var (result, id) = await medDataRepository.SaveCustomAnnotationAsync(user, dto.Id, dto.Term, dto.Coordinates);
            return result.Success ? Json(new { success = true, id }) : SaveResult(result);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Custom annotation save failed for image {MedDataId} and user {UserId}.", dto.Id, user.Id);
            return StatusCode(503, new { success = false, message = "The custom annotation could not be saved. Keep this page open and retry." });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Professional")]
    public async Task<IActionResult> DeleteCustomAnnotation([FromQuery] int customAnnotationId)
    {
        if (customAnnotationId <= 0) return BadRequest(new { success = false, message = "Invalid custom annotation." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });
        try { return SaveResult(await medDataRepository.DeleteCustomAnnotationAsync(user, customAnnotationId)); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Custom annotation {CustomAnnotationId} could not be deleted for user {UserId}.", customAnnotationId, user.Id);
            return StatusCode(503, new { success = false, message = "The custom annotation could not be deleted. Keep this page open and retry." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "Professional,Medical_Student")]
    public async Task<IActionResult> AssignmentStatus([FromQuery] int medDataId)
    {
        if (medDataId <= 0) return BadRequest(new { success = false, message = "Invalid image id." });
        // Claims only, so the open page's background check costs one indexed query and no user lookup.
        var userId = userManager.GetUserId(User);
        if (userId == null) return Unauthorized(new { success = false });
        var assigned = await medDataRepository.RefreshAssignmentAsync(medDataId, userId, User.IsInRole("Medical_Student"));
        return Json(new { success = true, assigned });
    }

    [HttpPost]
    [Authorize(Roles = "Medical_Student")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> SubmitStudentAnnotations([FromBody] StudentAnnotationList? annotationList)
    {
        if (!ModelState.IsValid || annotationList?.Annotations == null)
            return BadRequest(new { success = false, message = "Invalid annotation payload." });
        var annotations = annotationList.Annotations.ToList();
        if (annotations.Count == 0 || annotations.Any(item => item == null || item.Id <= 0))
            return BadRequest(new { success = false, message = "Add at least one valid annotation." });
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Your session has ended. Sign in again." });
        try
        {
            var result = await medDataRepository.SaveStudentAsync(user, annotations.Select(dto => new AnnotatedByStudentsMedData
            {
                MedDataId = dto.Id, Coordinates = dto.Coordinates, TextualAnnotation = dto.TextualAnnotation
            }).ToList());
            return result.Success ? Json(new { success = true, redirectUrl = "/Home/Student" }) : SaveResult(result);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Student annotation save failed for user {UserId}.", user.Id);
            return StatusCode(503, new { success = false, message = "The annotations could not be saved. Keep this page open and retry." });
        }
    }

    // Problem reports never depend on the image, the tools or the assignment, so they arrive even when those are broken.
    [HttpPost]
    [Authorize(Roles = "Professional,Medical_Student")]
    [EnableRateLimiting(ProblemReportRateLimit.PolicyName)]
    [RequestSizeLimit(64_000)]
    public async Task<IActionResult> ReportProblem([FromBody] ProblemReportDto? dto)
    {
        if (!ModelState.IsValid || dto == null || !ProblemReport.Kinds.Contains(dto.Kind)
            || (dto.Kind == ProblemReport.UserReport && string.IsNullOrWhiteSpace(dto.Message)))
            return BadRequest(new { success = false, message = "Describe the problem before sending it." });
        try
        {
            await medDataRepository.AddProblemReportAsync(new ProblemReport
            {
                CreatedAt = DateTime.UtcNow, UserId = userManager.GetUserId(User), MedDataId = dto.MedDataId > 0 ? dto.MedDataId : null,
                Kind = dto.Kind!, Term = dto.Term?.Trim(), Message = dto.Message?.Trim() ?? string.Empty, Details = dto.Details
            });
            return Json(new { success = true });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Problem report could not be stored for image {MedDataId}.", dto.MedDataId);
            return StatusCode(503, new { success = false, message = "The report could not be sent. Keep this page open and try again." });
        }
    }

    private IActionResult SaveResult(AnnotationSaveResult result) => result.Success
        ? Json(new { success = true })
        : StatusCode(result.Conflict ? 409 : 400, new { success = false, message = result.Message });
}
