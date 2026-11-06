using System.ComponentModel.DataAnnotations;

namespace MedAnnotateApp.Presentation.Dtos;

// Image modality is required. Speciality and body region are optional: an empty list includes all of its options.
public class ImageFiltersDto : IValidatableObject
{
    public const string ModalityRequired = "Select at least one image modality.";

    public string[]? Speciality { get; set; }

    public string[]? BodyRegion { get; set; }

    public string[]? ImageModality { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ImageModality is null || ImageModality.All(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult(ModalityRequired);
            yield break;
        }
        foreach (var (values, allowed, member, label) in new[]
        {
            (Speciality, ImageFilterOptions.Specialities, nameof(Speciality), "speciality"),
            (BodyRegion, ImageFilterOptions.BodyRegions, nameof(BodyRegion), "body region"),
            (ImageModality, ImageFilterOptions.ImageModalities, nameof(ImageModality), "image modality")
        })
        {
            if (values is null || values.Length == 0)
            {
                continue;
            }
            if (values.Any(value => string.IsNullOrWhiteSpace(value) || !allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)))
            {
                yield return new ValidationResult($"Choose a {label} from the list.", [member]);
            }
        }
    }

    public static string JoinSelection(IEnumerable<string>? values) => string.Join(",", (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase));
}

// Display labels of the MedAnnotate datasets (MedAnnotateDatasets/app_labels/label_vocabulary.csv, app-labels-1.1.0,
// scope "annotation"), chosen by the researcher on 2026-11-02. Image data stores these exact texts; matching ignores
// case and surrounding spaces. Keep these lists equal to the vocabulary: an image whose value is missing here can't be
// chosen by anyone. "Clinical photographs" (NEJM only) is added to ImageModalities when NEJM data enters the app.
public static class ImageFilterOptions
{
    public static readonly string[] Specialities =
    [
        "Cardiology", "Dentistry", "Dermatology", "Emergency/Critical Care", "Endocrinology",
        "Gastroenterology/Hepatology", "Hematology", "Immunology/Allergies", "Infectious diseases", "Neurology/Neurosurgery",
        "Obstetrics/Gynecology", "Oncology", "Ophthalmology", "Orthopedics", "Otorhinolaryngology",
        "Pediatrics", "Pulmonology", "Surgery", "Urology/Nephrology", "Vascular diseases", "Unknown"
    ];

    public static readonly string[] BodyRegions =
    [
        "Abdomen", "Brain", "Breast", "Chest and Thorax", "Eye orbit", "Head and Neck", "Lower extremity", "Pelvis",
        "Skin and Soft tissue", "Spine", "Upper Extremity", "Vascular", "Whole Body", "Unknown"
    ];

    public static readonly string[] ImageModalities =
        ["Angiography", "CT", "Endoscopy", "Fundoscopy", "Microscopy", "MRI", "Ultrasound", "X-ray"];
}
