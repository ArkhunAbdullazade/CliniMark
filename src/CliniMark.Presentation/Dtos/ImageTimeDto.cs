using System.ComponentModel.DataAnnotations;
using CliniMark.Infrastructure.Services;

namespace CliniMark.Presentation.Dtos;

/// <summary>The annotation page's running total of active seconds on its image since the page opened.</summary>
public class ImageTimeDto
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    public Guid View { get; set; }

    [Range(0, ImageTimeLog.MaxSeconds)]
    public int Seconds { get; set; }
}
