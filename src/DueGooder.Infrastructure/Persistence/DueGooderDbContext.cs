using Microsoft.EntityFrameworkCore;

namespace DueGooder.Infrastructure.Persistence;

/// <summary>SQLite schema for terms and sections. Natural keys are unique indexes over a surrogate primary key.</summary>
public sealed class DueGooderDbContext(DbContextOptions<DueGooderDbContext> options) : DbContext(options)
{
    #region State

    internal DbSet<TermRow> Terms => Set<TermRow>();

    internal DbSet<SectionRow> Sections => Set<SectionRow>();

    #endregion State

    #region Methods

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TermRow>(term =>
        {
            term.HasIndex(row => new { row.SchoolId, row.TermCode }).IsUnique();
        });

        modelBuilder.Entity<SectionRow>(section =>
        {
            section.HasIndex(row => new { row.SchoolId, row.TermCode, row.Subject, row.CourseNumber, row.SectionId })
                   .IsUnique();

            section.HasMany(row => row.Meetings)
                   .WithOne()
                   .HasForeignKey(row => row.SectionRowId)
                   .OnDelete(DeleteBehavior.Cascade);

            section.HasMany(row => row.Instructors)
                   .WithOne()
                   .HasForeignKey(row => row.SectionRowId)
                   .OnDelete(DeleteBehavior.Cascade);

            section.HasMany(row => row.Failures)
                   .WithOne()
                   .HasForeignKey(row => row.SectionRowId)
                   .OnDelete(DeleteBehavior.Cascade);
        });
    }

    #endregion Methods
}
