namespace DueGooder.Application;

/// <summary>Reads stored sections back out for normalized data export, independent of how they were written.</summary>
public interface IExportReader
{
    #region Methods

    /// <summary>Every stored section, optionally narrowed to a set of schools.</summary>
    Task<IReadOnlyList<ExportedSection>> GetSectionsAsync(IReadOnlySet<string>? schoolIds, CancellationToken cancellationToken);

    #endregion Methods
}
