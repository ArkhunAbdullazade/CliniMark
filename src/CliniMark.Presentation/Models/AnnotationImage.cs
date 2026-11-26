using CliniMark.Core.Models;
using CliniMark.Core.Services;

namespace CliniMark.Presentation.Models;

public sealed record AnnotationImage(int Id, string? ImageUrl)
{
    public static AnnotationImage? From(MedData? image) =>
        image == null ? null : new(image.Id, StudyImageAddress.For(image.Id, image.ImageUrl));
}
