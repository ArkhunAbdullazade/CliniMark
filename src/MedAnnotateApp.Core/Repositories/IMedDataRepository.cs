using MedAnnotateApp.Core.Models;

namespace MedAnnotateApp.Core.Repositories;

public interface IMedDataRepository
{
    Task<(MedData? MedData, string Counter)> GetNthMedDataBySpecialityAndPositionAsync(
        string? speciality,
        string? position,
        string? bodyRegion,
        string? imageModality,
        string userId);

    Task<IEnumerable<string?>> GetKeywordsByMedDataIdAsync(int id);
    Task<bool> UpdateLock(int medDataId, string keywordStates, bool isAnnotationStarted, bool isStudent, string userId);
    Task ReleaseAssignmentsAsync(string userId, bool isStudent);
    Task<AnnotationSaveResult> SaveProfessionalAsync(User user, AnnotatedMedData annotation);
    Task<AnnotationSaveResult> CompleteProfessionalAsync(int medDataId, User user);
    Task<AnnotationSaveResult> SkipImageAsync(int medDataId, User user);
    Task<AnnotationSaveResult> SaveStudentAsync(User user, IReadOnlyList<AnnotatedByStudentsMedData> annotations);
    Task<IReadOnlyList<AnnotatedMedData>?> GetProfessionalAnnotationsAsync(int medDataId, User user);
    Task<bool> RefreshAssignmentAsync(int medDataId, string userId, bool isStudent);
    Task<int> CountAvailableAsync(ImageSelectionFilters filters, bool isStudent, string userId);
    Task<int> CountCompletedByAsync(User user, bool isStudent);
    Task AddProblemReportAsync(ProblemReport report);
    Task<(AnnotationSaveResult Result, int? Id)> SaveCustomAnnotationAsync(User user, int medDataId, string? term, string? coordinates);
    Task<AnnotationSaveResult> DeleteCustomAnnotationAsync(User user, int customAnnotationId);
    Task<IReadOnlyList<CustomAnnotation>?> GetCustomAnnotationsAsync(int medDataId, User user);
    Task<AnnotationSaveResult> SaveMetadataChecksAsync(User user, int medDataId, IReadOnlyList<MetadataAnswer> answers);
    Task<IReadOnlyList<MetadataCheck>?> GetMetadataChecksAsync(int medDataId, User user);
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAddedMetadataValuesAsync();
}
