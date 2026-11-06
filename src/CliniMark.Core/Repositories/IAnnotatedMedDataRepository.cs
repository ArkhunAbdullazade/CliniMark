using CliniMark.Core.Models;

namespace CliniMark.Core.Repositories;

public interface IAnnotatedMedDataRepository
{
    Task<bool> CreateAsync(AnnotatedMedData annotatedMedData);
    Task<bool> CreateAllAsync(IEnumerable<AnnotatedMedData> annotatedMedDatas);
}
