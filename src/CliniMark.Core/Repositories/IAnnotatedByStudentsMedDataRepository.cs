using CliniMark.Core.Models;

namespace CliniMark.Core.Repositories;

public interface IAnnotatedByStudentsMedDataRepository
{
    Task<bool> CreateAsync(AnnotatedByStudentsMedData entity);
    Task<bool> CreateAllAsync(IEnumerable<AnnotatedByStudentsMedData> entities);
    Task<IEnumerable<AnnotatedByStudentsMedData>> GetAllAsync();
    Task<AnnotatedByStudentsMedData?> GetByIdAsync(int id);
    Task<bool> UpdateAsync(AnnotatedByStudentsMedData entity);
    Task<bool> DeleteAsync(int id);
}