using Microsoft.AspNetCore.Identity;

namespace CliniMark.Core.Models;
public class User : IdentityUser
{
    public string? FullName { get; set; }
    public string? University { get; set; }
    public string? Position { get; set; }
    public string? Speciality { get; set; }
    public string? BodyRegion { get; set; }
    public string? ImageModality { get; set; }
    public int ClinicalExperience { get; set; }
    public string? OrcidId { get; set; }
    public DateTime? CreatedAt { get; set; }
}