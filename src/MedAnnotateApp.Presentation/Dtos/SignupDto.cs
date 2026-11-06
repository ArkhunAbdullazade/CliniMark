using System.ComponentModel.DataAnnotations;
using MedAnnotateApp.Core.Services;

namespace MedAnnotateApp.Presentation.Dtos;

public class SignupDto : ImageFiltersDto
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [RegularExpression(EmailFormat.Pattern, ErrorMessage = "Enter a complete email address, such as name@example.org.")]
    [StringLength(EmailFormat.MaxLength, ErrorMessage = "Email must be at most 256 characters long")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Full name is required")]
    [StringLength(200, ErrorMessage = "Full name must be at most 200 characters long")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long")]
    public string? Password { get; set; }

    [Required(ErrorMessage = "Confirm password is required")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match")]
    public string? ConfirmPassword { get; set; }

    [Required(ErrorMessage = "Position is required")]
    [RegularExpression("^(specialist|general practitioner|resident/assistant|medical student)$", ErrorMessage = "Choose a position from the list.")]
    public string? Position { get; set; }

    [Required(ErrorMessage = "Enter your university or institution.")]
    [StringLength(300, ErrorMessage = "University or institution must be at most 300 characters long")]
    public string? University { get; set; }

    [StringLength(100, ErrorMessage = "ORCID iD must be at most 100 characters long")]
    public string? OrcidId { get; set; }
}
