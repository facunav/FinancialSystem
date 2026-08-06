using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Review;

/// <summary>
/// Cobertura de <see cref="MovementLoader"/> — documenta el comportamiento actual de
/// carga/adaptación de <see cref="BankStatement"/>/<see cref="Transaction"/> a
/// <see cref="FinancialMovement"/>, incluida la inversión de signo entre los dos
/// modelos (ver comentario en <c>MovementLoader.ToFinancialMovement(BankStatement)</c>).
/// Ningún test acá corrige comportamiento; donde algo llama la atención pero es
/// consistente con lo que el propio código documenta, se deja tal cual — ver PATCH-037.
///
/// Mismo patrón de <c>AppDbContext</c> InMemory que ya usa
/// <c>ClassifyMovementHandlerTests</c> — cada método abre su propio contexto sobre el
/// mismo nombre de base InMemory, para no arrastrar entidades trackeadas entre pasos.
/// </summary>
public class MovementLoaderTests
{
    private static readonly DateTime PeriodFrom = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodTo = new(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static MovementLoader CreateLoader(string dbName) => new(OpenDb(dbName));

    private static Task<IReadOnlyList<FinancialMovement>> LoadDefaultPeriodAsync(string dbName) =>
        CreateLoader(dbName).LoadAsync(
            DateOnly.FromDateTime(PeriodFrom), DateOnly.FromDateTime(PeriodTo));

    private static async Task<Guid> SeedBankStatementAsync(
        string dbName,
        DateTime date,
        decimal amount = 100m,
        string concept = "Movimiento de banco",
        Guid? financialAccountId = null,
        string? merchant = null,
        DateTime? merchantAtUtc = null,
        int? rowNumber = 3,
        string? sourceFile = "extracto.xls")
    {
        await using var db = OpenDb(dbName);
        var statement = new BankStatement
        {
            Date = date,
            Concept = concept,
            Amount = amount,
            Currency = "ARS",
            BankName = "BBVA",
            FinancialAccountId = financialAccountId,
            Merchant = merchant,
            MerchantAtUtc = merchantAtUtc,
            RowNumber = rowNumber,
            SourceFile = sourceFile,
        };
        db.BankStatements.Add(statement);
        await db.SaveChangesAsync();
        return statement.Id;
    }

    private static async Task<Guid> SeedTransactionAsync(
        string dbName,
        DateTime date,
        decimal amount = 100m,
        string description = "Movimiento de tarjeta",
        Guid? financialAccountId = null,
        string? couponNumber = "1234",
        string? rawLine = "raw line",
        string? sourceFile = "resumen.pdf")
    {
        await using var db = OpenDb(dbName);
        var transaction = new Transaction
        {
            Date = date,
            Description = description,
            Amount = amount,
            Currency = "ARS",
            FinancialAccountId = financialAccountId,
            CouponNumber = couponNumber,
            RawLine = rawLine,
            SourceFile = sourceFile,
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    private static async Task MarkAsClassifiedAsync(
        string dbName, SourceEntityType sourceEntityType, Guid sourceId)
    {
        await using var db = OpenDb(dbName);
        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = PeriodFrom,
            TotalAmount = 100m,
            Currency = "ARS",
            Description = "Ya clasificado",
            MovementType = MovementType.Purchase,
            FinancialImpact = FinancialImpact.Expense,
            Status = ClassificationStatus.Confirmed,
            ProcessingSource = ProcessingSource.ManualReview,
            CreatedAt = PeriodFrom,
            ProcessedAt = PeriodFrom,
        };
        db.ClassifiedMovements.Add(classifiedMovement);
        db.ClassifiedMovementItems.Add(new ClassifiedMovementItem
        {
            ClassifiedMovementId = classifiedMovement.Id,
            ClassifiedMovement = classifiedMovement,
            SourceEntityType = sourceEntityType,
            SourceId = sourceId,
            Role = MovementRole.Reference,
            OriginalAmount = 100m,
            OriginalDate = PeriodFrom,
            OriginalDescription = "Ya clasificado",
            OriginalCurrency = "ARS",
        });
        await db.SaveChangesAsync();
    }

    // ── Carga normal ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_CargaBankStatementsYTransactionsDelPeriodo()
    {
        var dbName = Guid.NewGuid().ToString();
        var bankId = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1));
        var transactionId = await SeedTransactionAsync(dbName, PeriodFrom.AddDays(2));

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.SourceId == bankId && m.Source == MovementSource.BankDebit);
        Assert.Contains(movements, m => m.SourceId == transactionId && m.Source == MovementSource.CreditCard);
    }

    [Fact]
    public async Task LoadAsync_PeriodoSinMovimientos_DevuelveListaVacia()
    {
        var dbName = Guid.NewGuid().ToString();

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Empty(movements);
    }

    // ── Filtro: ya clasificados quedan excluidos ────────────────────────────

    [Fact]
    public async Task LoadAsync_BankStatementYaClasificado_QuedaExcluido()
    {
        var dbName = Guid.NewGuid().ToString();
        var bankId = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1));
        await MarkAsClassifiedAsync(dbName, SourceEntityType.BankStatement, bankId);

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Empty(movements);
    }

    [Fact]
    public async Task LoadAsync_TransactionYaClasificada_QuedaExcluida()
    {
        var dbName = Guid.NewGuid().ToString();
        var transactionId = await SeedTransactionAsync(dbName, PeriodFrom.AddDays(1));
        await MarkAsClassifiedAsync(dbName, SourceEntityType.Transaction, transactionId);

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Empty(movements);
    }

    [Fact]
    public async Task LoadAsync_UnMovimientoClasificadoYOtroNo_SoloDevuelveElNoClasificado()
    {
        var dbName = Guid.NewGuid().ToString();
        var classifiedId = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), concept: "Clasificado");
        var pendingId = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), concept: "Pendiente");
        await MarkAsClassifiedAsync(dbName, SourceEntityType.BankStatement, classifiedId);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal(pendingId, movement.SourceId);
    }

    // ── Filtro de fechas ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_MovimientoAntesDelPeriodo_QuedaExcluido()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(-1));

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Empty(movements);
    }

    [Fact]
    public async Task LoadAsync_MovimientoDespuesDelPeriodo_QuedaExcluido()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedBankStatementAsync(dbName, PeriodTo.AddDays(1));

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Empty(movements);
    }

    [Fact]
    public async Task LoadAsync_MovimientoExactamenteEnElInicioDelPeriodo_QuedaIncluido()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedBankStatementAsync(dbName, PeriodFrom); // 00:00:00 del primer día

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Single(movements);
    }

    [Fact]
    public async Task LoadAsync_MovimientoEnHorarioTardioDelUltimoDia_QuedaIncluido()
    {
        // El límite superior se arma con TimeOnly.MaxValue (23:59:59.9999999) --
        // cualquier hora del último día del período debe quedar incluida, no solo
        // la medianoche.
        var dbName = Guid.NewGuid().ToString();
        var lateInTheLastDay = PeriodTo.AddHours(20);
        await SeedBankStatementAsync(dbName, lateInTheLastDay);

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Single(movements);
    }

    // ── Orden de salida ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_ElOrdenDeSalidaPrimeroBankStatementsLuegoTransactions()
    {
        // MovementLoader no aplica ningún ORDER BY propio -- concatena primero todos
        // los BankStatement y después todos los Transaction (ver LoadAsync). Se
        // siembra la Transaction antes que el BankStatement a propósito, para
        // confirmar que el orden de salida depende de la concatenación del código,
        // no del orden de inserción en la base.
        var dbName = Guid.NewGuid().ToString();
        var transactionId = await SeedTransactionAsync(dbName, PeriodFrom.AddDays(1));
        var bankId = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(2));

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Equal(2, movements.Count);
        Assert.Equal(bankId, movements[0].SourceId);
        Assert.Equal(MovementSource.BankDebit, movements[0].Source);
        Assert.Equal(transactionId, movements[1].SourceId);
        Assert.Equal(MovementSource.CreditCard, movements[1].Source);
    }

    // ── Duplicados a nivel de carga ──────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_DosBankStatementsConContenidoIdentico_DevuelveAmbosSinDeduplicar()
    {
        // MovementLoader no deduplica por contenido -- eso es responsabilidad de
        // ISuspicionDetector (ver SuspicionDetectorTests), no de este componente.
        // Dos filas con el mismo monto/fecha/descripción pero distinto Id se
        // devuelven ambas tal cual están en la base.
        var dbName = Guid.NewGuid().ToString();
        var id1 = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), amount: 250m, concept: "Igual");
        var id2 = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), amount: 250m, concept: "Igual");

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.SourceId == id1);
        Assert.Contains(movements, m => m.SourceId == id2);
    }

    // ── Caso especialmente importante: inversión de signo ───────────────────

    [Fact]
    public async Task LoadAsync_BankStatementCredito_MontoPositivoSeInvierteAIngresoNegativo()
    {
        // BankStatement: positivo = crédito/ingreso. FinancialMovement: positivo =
        // gasto/débito. MovementLoader invierte el signo a propósito al adaptar
        // (ver comentario en ToFinancialMovement(BankStatement)) -- un crédito
        // bancario de +100 se convierte en un FinancialMovement.Amount = -100.
        var dbName = Guid.NewGuid().ToString();
        await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), amount: 100m);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal(-100m, movement.Amount);
    }

    [Fact]
    public async Task LoadAsync_BankStatementDebito_MontoNegativoSeInvierteAGastoPositivo()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), amount: -100m);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal(100m, movement.Amount);
    }

    [Fact]
    public async Task LoadAsync_TransactionConMontoPositivo_MantieneElSignoSinInvertir()
    {
        // Transaction ya sigue la convención de FinancialMovement (positivo =
        // gasto/débito) -- MovementLoader no invierte nada al adaptarla.
        var dbName = Guid.NewGuid().ToString();
        await SeedTransactionAsync(dbName, PeriodFrom.AddDays(1), amount: 100m);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal(100m, movement.Amount);
    }

    [Fact]
    public async Task LoadAsync_TransactionConMontoNegativo_MantieneElSignoSinInvertir()
    {
        // Documentado tal cual, aunque sea un caso menos frecuente en la práctica:
        // MovementLoader no aplica ninguna regla de signo condicional sobre
        // Transaction -- el valor pasa exactamente igual, sea cual sea su signo.
        var dbName = Guid.NewGuid().ToString();
        await SeedTransactionAsync(dbName, PeriodFrom.AddDays(1), amount: -50m);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal(-50m, movement.Amount);
    }

    // ── Mapeo de campos ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_MapeaCamposDeBankStatement()
    {
        var dbName = Guid.NewGuid().ToString();
        var financialAccountId = Guid.NewGuid();
        var merchantAt = PeriodFrom.AddHours(3);
        await SeedBankStatementAsync(
            dbName, PeriodFrom.AddDays(1), amount: 100m, concept: "PAGO CON VISA DEBITO",
            financialAccountId: financialAccountId, merchant: "FARMACITY", merchantAtUtc: merchantAt,
            rowNumber: 7, sourceFile: "extracto-enero.xls");

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal("PAGO CON VISA DEBITO", movement.Description);
        Assert.Equal("7", movement.OriginalId);
        Assert.Equal("extracto-enero.xls", movement.SourceFile);
        Assert.Equal(financialAccountId, movement.FinancialAccountId);
        Assert.Equal("FARMACITY", movement.Merchant);
        Assert.Equal(merchantAt, movement.MerchantAtUtc);
        Assert.Equal(MovementSource.BankDebit, movement.Source);
    }

    [Fact]
    public async Task LoadAsync_MapeaCamposDeTransaction()
    {
        var dbName = Guid.NewGuid().ToString();
        var financialAccountId = Guid.NewGuid();
        await SeedTransactionAsync(
            dbName, PeriodFrom.AddDays(1), amount: 250m, description: "FARMACITY SA",
            financialAccountId: financialAccountId, couponNumber: "9988", rawLine: "linea cruda",
            sourceFile: "resumen-visa.pdf");

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Equal("FARMACITY SA", movement.Description);
        Assert.Equal("9988", movement.OriginalId);
        Assert.Equal("linea cruda", movement.RawLine);
        Assert.Equal("resumen-visa.pdf", movement.SourceFile);
        Assert.Equal(financialAccountId, movement.FinancialAccountId);
        Assert.Equal(MovementSource.CreditCard, movement.Source);
        Assert.Null(movement.Merchant); // Transaction no tiene enriquecimiento de comercio
    }

    // ── Datos incompletos ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_BankStatementSinCamposOpcionales_NoFallaYQuedanNulos()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedBankStatementAsync(
            dbName, PeriodFrom.AddDays(1), financialAccountId: null, merchant: null,
            merchantAtUtc: null, rowNumber: null, sourceFile: null);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Null(movement.FinancialAccountId);
        Assert.Null(movement.Merchant);
        Assert.Null(movement.MerchantAtUtc);
        Assert.Null(movement.OriginalId); // RowNumber null -> OriginalId null
        Assert.Null(movement.SourceFile);
    }

    [Fact]
    public async Task LoadAsync_TransactionSinCamposOpcionales_NoFallaYQuedanNulos()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedTransactionAsync(
            dbName, PeriodFrom.AddDays(1), financialAccountId: null, couponNumber: null,
            rawLine: null, sourceFile: null);

        var movements = await LoadDefaultPeriodAsync(dbName);

        var movement = Assert.Single(movements);
        Assert.Null(movement.FinancialAccountId);
        Assert.Null(movement.OriginalId);
        Assert.Null(movement.RawLine);
        Assert.Null(movement.SourceFile);
    }

    // ── Múltiples cuentas financieras ────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_MovimientosDeDistintasCuentasFinancieras_SeMapeanIndependientemente()
    {
        var dbName = Guid.NewGuid().ToString();
        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();
        var id1 = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), financialAccountId: account1);
        var id2 = await SeedBankStatementAsync(dbName, PeriodFrom.AddDays(1), financialAccountId: account2);

        var movements = await LoadDefaultPeriodAsync(dbName);

        Assert.Equal(2, movements.Count);
        Assert.Equal(account1, movements.Single(m => m.SourceId == id1).FinancialAccountId);
        Assert.Equal(account2, movements.Single(m => m.SourceId == id2).FinancialAccountId);
    }
}
