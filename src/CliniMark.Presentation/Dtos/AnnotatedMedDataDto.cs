using System.ComponentModel.DataAnnotations;

namespace CliniMark.Presentation.Dtos;

public class AnnotatedMedDataDto
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid medical data id is required")]
    public int Id { get; set; }

    public string? ImageUrl { get; set; }
    public string? ImageDescription { get; set; }
    public string? Sex { get; set; }
    public string? Age { get; set; }
    public string? SkinTone { get; set; }
    public string? BodyRegion { get; set; }
    public string? Diagnosis { get; set; }
    public string? TreatmentName { get; set; }
    public string? Speciality { get; set; }
    public string? Modality { get; set; }
    [StringLength(1_500_000)]
    public string? BoxCoordinates { get; set; }
    [Required, StringLength(1000)]
    public string? ExtractedKeyword { get; set; }
    [Required, StringLength(100)]
    public string? PressedButton { get; set; }
    [StringLength(100)]
    public string? Timestamps { get; set; }
    [StringLength(4000)]
    public string? Comment { get; set; }
    [StringLength(4000)]
    public string? TechnicalProblem { get; set; }
    public string? KeywordStates { get; set; }
}
