namespace FinancialSystem.Application.Imports;

public sealed class FileIngestionOptions
{
    public const string SectionName = "FileIngestion";

    public string? ImportsPath { get; set; }

    public int DebounceMilliseconds { get; set; } = 750;

    public static readonly string[] WatchedExtensions = [".pdf", ".csv", ".xlsx", ".xls"];

    public string[] LegacyExpenseFilePatterns { get; set; } = ["Cuentas*.xlsx"];

    public string[] BbvaBankStatementFilePatterns { get; set; } =
       ["Caja*.xls", "*ahorros*.xls", "*corriente*.xls"];

    public string[] BbvaDebitCardFilePatterns { get; set; } =
       ["*ltimos_movimientos*.xlsx", "*Movimientos*Debito*.xlsx"];
}
