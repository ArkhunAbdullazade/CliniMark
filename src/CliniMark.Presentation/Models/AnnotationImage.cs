using CliniMark.Core.Models;

namespace CliniMark.Presentation.Models;

public sealed record AnnotationImage(int Id, string? ImageUrl)
{
    public static AnnotationImage? From(MedData? image) =>
        image == null ? null : new(image.Id, image.ImageUrl);
}
