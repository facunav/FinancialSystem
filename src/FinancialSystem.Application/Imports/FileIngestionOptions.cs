namespace FinancialSystem.Application.Imports;

public sealed class FileIngestionOptions
{
    public const string SectionName = "FileIngestion";

    public string? ImportsPath { get; set; }

    public int DebounceMilliseconds { get; set; } = 750;

    public static readonly string[] WatchedExtensions = [".pdf", ".csv", ".xlsx"];
}
