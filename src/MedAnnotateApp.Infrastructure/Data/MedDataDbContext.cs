using MedAnnotateApp.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MedAnnotateApp.Infrastructure.Data;

public class MedDataDbContext : IdentityDbContext<User, IdentityRole, string>
{
    public MedDataDbContext(DbContextOptions<MedDataDbContext> options) : base(options)
    {
    }

    public DbSet<MedData> MedDatas => Set<MedData>();
    public DbSet<MedDataKeyword> MedDataKeywords => Set<MedDataKeyword>();
    public DbSet<AnnotatedMedData> AnnotatedMedDatas => Set<AnnotatedMedData>();
    public DbSet<AnnotatedByStudentsMedData> AnnotatedByStudentsMedDatas => Set<AnnotatedByStudentsMedData>();
    public DbSet<AdminAction> AdminActions => Set<AdminAction>();
    public DbSet<ProblemReport> ProblemReports => Set<ProblemReport>();
    public DbSet<CustomAnnotation> CustomAnnotations => Set<CustomAnnotation>();
    public DbSet<MetadataCheck> MetadataChecks => Set<MetadataCheck>();
    public DbSet<ImageSkip> ImageSkips => Set<ImageSkip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MedData>()
            .Property(medData => medData.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<MedDataKeyword>()
            .HasOne(medDataKeyword => medDataKeyword.MedData)
            .WithMany(medData => medData.MedDataKeywords)
            .HasForeignKey(medDataKeyword => medDataKeyword.MedDataId);

        modelBuilder.Entity<MedData>()
            .Property(medData => medData.IsAnnotated)
            .HasDefaultValue(false);

        // Lock lookups run on every annotation page load; result lookups back the admin panel and exports.
        modelBuilder.Entity<MedData>().HasIndex(medData => medData.LockedByUserId);
        modelBuilder.Entity<MedData>().HasIndex(medData => medData.LockedByStudentUserId);
        modelBuilder.Entity<AnnotatedMedData>().HasIndex(annotation => annotation.MedDataId);
        modelBuilder.Entity<AnnotatedMedData>().HasIndex(annotation => annotation.Email);
        modelBuilder.Entity<AnnotatedMedData>().HasIndex(annotation => annotation.CreatedAt);
        modelBuilder.Entity<AnnotatedByStudentsMedData>().HasIndex(annotation => annotation.MedDataId);
        modelBuilder.Entity<AnnotatedByStudentsMedData>().HasIndex(annotation => annotation.CreatedAt);
        modelBuilder.Entity<AdminAction>().HasIndex(action => action.At);
        modelBuilder.Entity<ProblemReport>().HasIndex(report => report.CreatedAt);
        modelBuilder.Entity<ProblemReport>().HasIndex(report => report.ResolvedAt);
        modelBuilder.Entity<CustomAnnotation>().HasIndex(custom => custom.MedDataId);
        modelBuilder.Entity<CustomAnnotation>().HasIndex(custom => custom.CreatedAt);
        modelBuilder.Entity<MetadataCheck>().HasIndex(check => new { check.MedDataId, check.UserId, check.Field }).IsUnique();
        modelBuilder.Entity<MetadataCheck>().HasIndex(check => check.IsNewValue);
        modelBuilder.Entity<ImageSkip>().HasIndex(skip => new { skip.MedDataId, skip.UserId }).IsUnique();
    }
}
