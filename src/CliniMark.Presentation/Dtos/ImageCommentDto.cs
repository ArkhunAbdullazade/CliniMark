using System.ComponentModel.DataAnnotations;
using CliniMark.Core.Models;

namespace CliniMark.Presentation.Dtos;

/// <summary>A specialist's comment on the whole image (ImageComment); empty text removes it.</summary>
public class ImageCommentDto
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid medical data id is required")]
    public int Id { get; set; }

    [StringLength(ImageComment.MaxLength, ErrorMessage = "Keep the comment below 4,000 characters.")]
    public string? Comment { get; set; }
}
