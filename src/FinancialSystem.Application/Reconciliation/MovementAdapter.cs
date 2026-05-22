using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation;

/// <summary>
/// Convierte entidades del sistema existente (Transaction) al modelo
/// unificado de conciliación (FinancialMovement).
///
/// PRINCIPIO: la conciliación no conoce Transaction. Este adaptador
/// es el único punto de acoplamiento entre las dos capas.
/// Si Transaction cambia, sólo hay que tocar este archivo.
///
/// DECISIONES DE MAPEO:
///   - Source se infiere del SourceFile cuando está disponible
///   - PaymentMethod es null para banco/tarjeta (no se carga allí)
///   - Amount siempre positivo para gastos (la entidad ya lo normaliza)
/// </summary>
public static class MovementAdapter
{
    /// <summary>
    /// Convierte una Transaction a FinancialMovement infiriendo la fuente
    /// a partir del nombre del archivo de origen.
    /// </summary>
    public static FinancialMovement FromTransaction(Transaction transaction, MovementSource? overrideSource = null)
    {
        var source = overrideSource ?? InferSource(transaction.SourceFile);

        return new FinancialMovement
        {
            Id = transaction.Id,
            Date = transaction.Date,
            Description = transaction.Description,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Source = source,
            OriginalId = transaction.CouponNumber,
            SourceFile = transaction.SourceFile,
            RawLine = transaction.RawLine,
        };
    }

    /// <summary>
    /// Crea un movimiento manual con método de pago explícito.
    /// Útil cuando el Excel tiene columna de método de pago.
    /// </summary>
    public static FinancialMovement FromManualEntry(
        Transaction transaction,
        MovementSource source,
        PaymentMethod? paymentMethod = null,
        MovementCategory category = MovementCategory.Unknown,
        string? sheetName = null)
    {
        return new FinancialMovement
        {
            Id = transaction.Id,
            Date = transaction.Date,
            Description = transaction.Description,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Source = source,
            Category = category,
            PaymentMethod = paymentMethod,
            OriginalId = transaction.CouponNumber,
            SourceFile = transaction.SourceFile,
            RawLine = transaction.RawLine,
            SheetName = sheetName,
        };
    }

    // ── Inferencia de fuente ──────────────────────────────────────

    private static MovementSource InferSource(string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
            return MovementSource.BankDebit;

        var fileName = Path.GetFileNameWithoutExtension(sourceFile)
            .ToUpperInvariant();
        var ext = Path.GetExtension(sourceFile).ToUpperInvariant();

        // PDFs son siempre tarjetas
        if (ext == ".PDF") return MovementSource.CreditCard;

        // Excel: inferir por nombre de archivo
        if (fileName.Contains("MANUAL") || fileName.Contains("PERSONAL"))
            return MovementSource.ManualDynamic;

        if (fileName.Contains("FIJO") || fileName.Contains("FIXED") || fileName.Contains("RECURRENTE"))
            return MovementSource.ManualFixed;

        if (fileName.Contains("BANCO") || fileName.Contains("BANK") ||
            fileName.Contains("DEBITO") || fileName.Contains("DEBIT"))
            return MovementSource.BankDebit;

        // Default: banco (la fuente más "segura" para conciliación)
        return MovementSource.BankDebit;
    }

    /// <summary>
    /// Inferencia de PaymentMethod desde texto libre (columna del Excel manual).
    /// </summary>
    public static PaymentMethod? ParsePaymentMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var normalized = raw.Trim().ToUpperInvariant();

        return normalized switch
        {
            "DEBITO" or "DÉBITO" or "DB" or "DEB" or "DEBIT" => PaymentMethod.Debit,
            "CREDITO" or "CRÉDITO" or "CR" or "CRED" or "TC" or "CREDIT" => PaymentMethod.Credit,
            "EFECTIVO" or "EFE" or "CASH" => PaymentMethod.Cash,
            "TRANSFERENCIA" or "TRANS" or "TRF" or "TRANSFER" => PaymentMethod.Transfer,
            _ => null,
        };
    }

    /// <summary>
    /// Inferencia de categoría desde descripción (heurística simple, sin ML).
    /// Extendible: agregar keywords según el vocabulario del usuario.
    /// </summary>
    public static MovementCategory InferCategory(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return MovementCategory.Unknown;

        var upper = description.ToUpperInvariant();

        if (ContainsAny(upper, "NETFLIX", "SPOTIFY", "DISNEY", "HBO", "AMAZON PRIME", "MUBI", "GYM", "GIMNASIO"))
            return MovementCategory.Subscription;

        if (ContainsAny(upper, "FARMACI", "DROGUERIA", "MEDICAMENTO", "SALUD"))
            return MovementCategory.Health;

        if (ContainsAny(upper, "UBER", "CABIFY", "TAXI", "COLECTIVO", "SUBTE", "PEAJE", "YPF", "SHELL"))
            return MovementCategory.Transport;

        if (ContainsAny(upper, "SUPERMERCADO", "CARREFOUR", "COTO", "DISCO", "JUMBO", "DIA",
            "PEDIDOSYA", "RAPPI", "DELIVERY", "MCDONALD", "BURGER", "SUBWAY"))
            return MovementCategory.Food;

        if (ContainsAny(upper, "EDESUR", "EDENOR", "METROGAS", "AYSA", "TELECOM", "MOVISTAR", "CLARO", "PERSONAL"))
            return MovementCategory.Services;

        if (ContainsAny(upper, "SEGURO", "ZURICH", "SANCOR", "MAPFRE"))
            return MovementCategory.Insurance;

        if (ContainsAny(upper, "COLEGIO", "ESCUELA", "UNIVERSIDAD", "CUOTA"))
            return MovementCategory.Education;

        return MovementCategory.Unknown;
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.Ordinal));

    /// <summary>
    /// Convierte ManualExpense → FinancialMovement para el motor de conciliación.
    /// Único punto de acoplamiento entre ManualExpense y la capa de conciliación.
    /// </summary>
    public static FinancialMovement FromManualExpense(ManualExpense expense)
    {
        var source = expense.Sheet switch
        {
            ManualExpenseSheet.Dynamic => MovementSource.ManualDynamic,
            ManualExpenseSheet.Fixed => MovementSource.ManualFixed,
            _ => MovementSource.ManualDynamic,
        };

        var paymentMethod = expense.PaymentMethod.ToUpperInvariant() switch
        {
            "DEBITO" => (PaymentMethod?)PaymentMethod.Debit,
            "CREDITO" => PaymentMethod.Credit,
            "EFECTIVO" => PaymentMethod.Cash,
            "TRANSFERENCIA" => PaymentMethod.Transfer,
            _ => null,
        };

        // La descripción para matching = Category.
        // Si hay Notes, se concatena para que DescriptionMatchingRule
        // tenga más tokens contra los que comparar.
        var description = string.IsNullOrWhiteSpace(expense.Notes)
            ? expense.Category
            : $"{expense.Category} {expense.Notes}";

        return new FinancialMovement
        {
            Id = expense.Id,
            Date = expense.Date,
            Description = description,
            Amount = expense.Amount,
            Currency = expense.Currency,
            Source = source,
            Category = InferCategoryFromManual(expense.Category),
            PaymentMethod = paymentMethod,
            OriginalId = expense.ExternalId,
            SourceFile = expense.SourceFile,
            SheetName = expense.SheetName,
        };
    }

    private static MovementCategory InferCategoryFromManual(string category) =>
        category.ToUpperInvariant() switch
        {
            "ALMACEN" => MovementCategory.Food,
            "FARMACIA" => MovementCategory.Health,
            "MEDICOS" => MovementCategory.Health,
            "NAFTA" => MovementCategory.Transport,
            "LUZ" => MovementCategory.Services,
            "GAS" => MovementCategory.Services,
            "OSSE" => MovementCategory.Services,
            "CABLE / INTERNET" => MovementCategory.Services,
            "MOVISTAR FACU" or "MOVISTAR TATI" => MovementCategory.Services,
            "SEGURO AUTO" or "SEGURO CASA"
                          or "SEGURO VIDA" => MovementCategory.Insurance,
            "ALQUILER" or "EXPENSAS"
                       or "BAULERA" => MovementCategory.Other,
            "VISA BBVA" or "VISA BAPRO"
                           or "VISA GALICIA"
                           or "MASTERCARD BBVA"
                           or "MASTERCARD GALICIA"
                           or "BBVA CUENTA" => MovementCategory.Transfer,
            _ => MovementCategory.Unknown,
        };
}
