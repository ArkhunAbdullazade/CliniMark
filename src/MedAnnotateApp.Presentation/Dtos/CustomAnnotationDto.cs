using System.ComponentModel.DataAnnotations;

namespace MedAnnotateApp.Presentation.Dtos;

public class CustomAnnotationDto
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid medical data id is required")]
    public int Id { get; set; }
    [Required, StringLength(1000)]
    public string? Term { get; set; }
    [Required, StringLength(1_500_000)]
    public string? Coordinates { get; set; }
}
