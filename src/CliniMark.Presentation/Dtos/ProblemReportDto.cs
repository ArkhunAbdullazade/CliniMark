using System.ComponentModel.DataAnnotations;

namespace CliniMark.Presentation.Dtos;

public class ProblemReportDto
{
    public int? MedDataId { get; set; }
    [Required, StringLength(40)]
    public string? Kind { get; set; }
    [StringLength(1000)]
    public string? Term { get; set; }
    [StringLength(4000)]
    public string? Message { get; set; }
    [StringLength(8000)]
    public string? Details { get; set; }
}
