using CliniMark.Core.Models;
using CliniMark.Core.Repositories;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CliniMark.Infrastructure.Repositories;

public class AnnotatedByStudentsMedDataRepository : IAnnotatedByStudentsMedDataRepository
{
    private readonly MedDataDbContext context;
    private readonly ILogger<AnnotatedByStudentsMedDataRepository> logger;

    public AnnotatedByStudentsMedDataRepository(
        MedDataDbContext context,
        ILogger<AnnotatedByStudentsMedDataRepository> logger)
    {
        this.context = context;
        this.logger = logger;
    }

    public async Task<bool> CreateAsync(AnnotatedByStudentsMedData entity)
    {
        try
        {
            await context.AnnotatedByStudentsMedDatas.AddAsync(entity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create student annotation for MedDataId {MedDataId}.", entity.MedDataId);
            return false;
        }
    }

    public async Task<bool> CreateAllAsync(IEnumerable<AnnotatedByStudentsMedData> entities)
    {
        var entityList = entities.ToList();

        try
        {
            await context.AnnotatedByStudentsMedDatas.AddRangeAsync(entityList);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create {Count} student annotations.", entityList.Count);
            return false;
        }
    }

    public async Task<IEnumerable<AnnotatedByStudentsMedData>> GetAllAsync()
    {
        return await context.AnnotatedByStudentsMedDatas.ToListAsync();
    }

    public async Task<AnnotatedByStudentsMedData?> GetByIdAsync(int id)
    {
        return await context.AnnotatedByStudentsMedDatas.FindAsync(id);
    }

    public async Task<bool> UpdateAsync(AnnotatedByStudentsMedData entity)
    {
        try
        {
            context.AnnotatedByStudentsMedDatas.Update(entity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update student annotation {AnnotationId}.", entity.Id);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var entity = await GetByIdAsync(id);
            if (entity == null)
            {
                return false;
            }

            context.AnnotatedByStudentsMedDatas.Remove(entity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete student annotation {AnnotationId}.", id);
            return false;
        }
    }
}
