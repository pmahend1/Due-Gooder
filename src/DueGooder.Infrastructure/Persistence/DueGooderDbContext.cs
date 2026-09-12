using Microsoft.Data.Sqlite;
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

    /// <summary>Creates the database if it doesn't exist, then checks that its schema is the current one.</summary>
    /// <returns>Why the existing file can't be used, or null when it can.</returns>
    public async Task<string?> EnsureCurrentSchemaAsync(CancellationToken cancellationToken)
    {
        await Database.EnsureCreatedAsync(cancellationToken);

        // EnsureCreated never alters an existing file, so a database from before a schema change would fail mid-run instead.
        try
        {
            _ = await Sections.Select(row => row.LastConfirmedAt).FirstOrDefaultAsync(cancellationToken);
            _ = await Terms.Select(row => row.GapDetail).FirstOrDefaultAsync(cancellationToken);
            return null;
        }
        catch (SqliteException exception)
        {
            return $"the database has an older schema ({exception.Message})";
        }
    }

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
