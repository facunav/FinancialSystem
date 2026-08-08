namespace FinancialSystem.Application.Imports;

public interface IImportFileSink
{
    /// <summary>
    /// Patch 0105 (vínculo ImportBatchId): <paramref name="importBatchId"/> es el mismo Id
    /// ya generado por FileImportRouter para esta corrida (ver IFileImportHandler.HandleAsync)
    /// -- se estampa en cada Transaction nueva que este sink inserte, antes de agregarla al
    /// DbSet.
    /// </summary>
    Task<ImportRunResult> HandleFileAsync(string filePath, Guid importBatchId, CancellationToken cancellationToken = default);
}
