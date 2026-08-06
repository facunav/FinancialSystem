using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Review;

/// <summary>
/// Cobertura de <see cref="ReviewEngine"/>: el orquestador que combina
/// <see cref="IMovementLoader"/> e <see cref="ISuspicionDetector"/> en un
/// <see cref="ReviewResult"/> (ver ReviewEngine.cs -- GenerateAsync no hace más que
/// delegar en ambas dependencias y ensamblar el resultado).
///
/// Dos secciones:
///  - Ejecución en aislamiento, con fakes propios de <see cref="IMovementLoader"/> e
///    <see cref="ISuspicionDetector"/>, para verificar solo la lógica de orquestación.
///  - Integración real, wireando <see cref="MovementLoader"/> y
///    <see cref="Infrastructure.Review.SuspicionDetector"/> de verdad contra un
///    <see cref="AppDbContext"/> InMemory, incluido el caso especialmente importante de
///    la inversión de signo (ver MovementLoaderTests para la cobertura dedicada de esa
///    regla a nivel del loader solo).
///
/// Ningún test acá corrige comportamiento ni modifica ReviewEngine/MovementLoader/
/// SuspicionDetector -- ver PATCH-037.
/// </summary>
public class ReviewEngineTests
{
    private static readonly DateOnly From = new(2026, 1, 10);
    private static readonly DateOnly To = new(2026, 1, 20);

    private static FinancialMovement Movement(
        decimal amount = 100m, DateTime? date = null, string description = "Movimiento") => new()
    {
        SourceId = Guid.NewGuid(),
        Date = date ?? new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        Description = description,
        Amount = amount,
        Source = MovementSource.BankDebit,
    };

    private static SuspiciousGroup Group(params FinancialMovement[] movements) => new()
    {
        Movements = movements,
        Reason = SuspicionReason.PossibleDuplicate,
        Description = "Grupo de prueba",
    };

    // ── Fakes de IMovementLoader / ISuspicionDetector ────────────────────────

    private sealed class FakeMovementLoader(IReadOnlyList<FinancialMovement> movements) : IMovementLoader
    {
        public DateOnly? ReceivedFrom { get; private set; }
        public DateOnly? ReceivedTo { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<IReadOnlyList<FinancialMovement>> LoadAsync(
            DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            ReceivedFrom = from;
            ReceivedTo = to;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(movements);
        }
    }

    private sealed class FakeSuspicionDetector(IReadOnlyList<SuspiciousGroup> groups) : ISuspicionDetector
    {
        public IReadOnlyList<FinancialMovement>? ReceivedMovements { get; private set; }

        public IReadOnlyList<SuspiciousGroup> Detect(IReadOnlyList<FinancialMovement> movements)
        {
            ReceivedMovements = movements;
            return groups;
        }
    }

    // ── Ejecución normal: paso de parámetros entre las dependencias ─────────

    [Fact]
    public async Task GenerateAsync_PasaFromYToSinModificarAlLoader()
    {
        var loader = new FakeMovementLoader([]);
        var engine = new ReviewEngine(loader, new FakeSuspicionDetector([]));

        await engine.GenerateAsync(From, To);

        Assert.Equal(From, loader.ReceivedFrom);
        Assert.Equal(To, loader.ReceivedTo);
    }

    [Fact]
    public async Task GenerateAsync_PropagaElCancellationTokenAlLoader()
    {
        var loader = new FakeMovementLoader([]);
        var engine = new ReviewEngine(loader, new FakeSuspicionDetector([]));
        using var cts = new CancellationTokenSource();

        await engine.GenerateAsync(From, To, cts.Token);

        Assert.Equal(cts.Token, loader.ReceivedCancellationToken);
    }

    [Fact]
    public async Task GenerateAsync_PasaLaSalidaExactaDelLoaderAlDetector()
    {
        var movement = Movement();
        var loader = new FakeMovementLoader([movement]);
        var detector = new FakeSuspicionDetector([]);
        var engine = new ReviewEngine(loader, detector);

        await engine.GenerateAsync(From, To);

        var received = Assert.Single(detector.ReceivedMovements!);
        Assert.Same(movement, received);
    }

    // ── Generación de resultados ──────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_ArmaReviewResultConPeriodoYMovementsDelLoader()
    {
        var movement = Movement();
        var engine = new ReviewEngine(new FakeMovementLoader([movement]), new FakeSuspicionDetector([]));

        var result = await engine.GenerateAsync(From, To);

        Assert.Equal(From, result.PeriodStart);
        Assert.Equal(To, result.PeriodEnd);
        var resultMovement = Assert.Single(result.Movements);
        Assert.Same(movement, resultMovement);
    }

    [Fact]
    public async Task GenerateAsync_ElapsedNoEsNegativo()
    {
        var engine = new ReviewEngine(new FakeMovementLoader([]), new FakeSuspicionDetector([]));

        var result = await engine.GenerateAsync(From, To);

        Assert.True(result.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_GeneratedAtSeAsignaAutomaticamenteAlMomentoDeCrearElResultado()
    {
        var before = DateTimeOffset.UtcNow;
        var engine = new ReviewEngine(new FakeMovementLoader([]), new FakeSuspicionDetector([]));

        var result = await engine.GenerateAsync(From, To);

        var after = DateTimeOffset.UtcNow;
        Assert.InRange(result.GeneratedAt, before, after);
    }

    // ── Sin hallazgos / múltiples hallazgos ───────────────────────────────────

    [Fact]
    public async Task GenerateAsync_SinHallazgos_SuspiciousQuedaVacio()
    {
        var engine = new ReviewEngine(new FakeMovementLoader([Movement()]), new FakeSuspicionDetector([]));

        var result = await engine.GenerateAsync(From, To);

        Assert.Empty(result.Suspicious);
    }

    [Fact]
    public async Task GenerateAsync_MultiplesHallazgos_SeIncluyenTodosEnElResultado()
    {
        var groupA = Group(Movement(), Movement());
        var groupB = Group(Movement(), Movement());
        var engine = new ReviewEngine(new FakeMovementLoader([]), new FakeSuspicionDetector([groupA, groupB]));

        var result = await engine.GenerateAsync(From, To);

        Assert.Equal(2, result.Suspicious.Count);
        Assert.Contains(groupA, result.Suspicious);
        Assert.Contains(groupB, result.Suspicious);
    }

    // ── Casos límite ────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_LoaderDevuelveVacio_NoFallaYResultadoQuedaVacio()
    {
        var engine = new ReviewEngine(new FakeMovementLoader([]), new FakeSuspicionDetector([]));

        var result = await engine.GenerateAsync(From, To);

        Assert.Empty(result.Movements);
        Assert.Empty(result.Suspicious);
    }

    [Fact]
    public async Task GenerateAsync_UnUnicoMovimiento_SeReflejaEnElResultadoSinFormarGrupos()
    {
        var movement = Movement();
        var engine = new ReviewEngine(new FakeMovementLoader([movement]), new FakeSuspicionDetector([]));

        var result = await engine.GenerateAsync(From, To);

        var resultMovement = Assert.Single(result.Movements);
        Assert.Same(movement, resultMovement);
        Assert.Empty(result.Suspicious);
    }

    // ── Integración real: MovementLoader + SuspicionDetector + ReviewEngine ──

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ReviewEngine CreateRealEngine(string dbName, ReviewEngineOptions? options = null)
    {
        var loader = new MovementLoader(OpenDb(dbName));
        var detector = new SuspicionDetector(Options.Create(options ?? new ReviewEngineOptions()));
        return new ReviewEngine(loader, detector);
    }

    private static async Task MarkAsClassifiedAsync(string dbName, SourceEntityType sourceEntityType, Guid sourceId)
    {
        await using var db = OpenDb(dbName);
        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = 100m,
            Currency = "ARS",
            Description = "Ya clasificado",
            MovementType = MovementType.Purchase,
            FinancialImpact = FinancialImpact.Expense,
            Status = ClassificationStatus.Confirmed,
            ProcessingSource = ProcessingSource.ManualReview,
            CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            ProcessedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
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
            OriginalDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            OriginalDescription = "Ya clasificado",
            OriginalCurrency = "ARS",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GenerateAsync_IntegracionReal_BaseVacia_DevuelveResultadoVacioSinFallar()
    {
        var dbName = Guid.NewGuid().ToString();
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Empty(result.Movements);
        Assert.Empty(result.Suspicious);
    }

    [Fact]
    public async Task GenerateAsync_IntegracionReal_DetectaDuplicadosEntreMovimientosCargadosDelPeriodo()
    {
        var dbName = Guid.NewGuid().ToString();
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = OpenDb(dbName))
        {
            db.Transactions.Add(new Transaction { Date = date, Description = "Compra A", Amount = 500m, Currency = "ARS" });
            db.Transactions.Add(new Transaction { Date = date, Description = "Compra A", Amount = 500m, Currency = "ARS" });
            await db.SaveChangesAsync();
        }
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(2, result.Movements.Count);
        var group = Assert.Single(result.Suspicious);
        Assert.Equal(SuspicionReason.PossibleDuplicate, group.Reason);
        Assert.Equal(2, group.Movements.Count);
    }

    [Fact]
    public async Task GenerateAsync_IntegracionReal_CombinaBankStatementsYTransactionsDelMismoPeriodo()
    {
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var dbName = Guid.NewGuid().ToString();
        await using (var db = OpenDb(dbName))
        {
            db.BankStatements.Add(new BankStatement { Date = date, Concept = "Movimiento banco", Amount = 200m, Currency = "ARS", BankName = "BBVA" });
            db.Transactions.Add(new Transaction { Date = date, Description = "Movimiento tarjeta", Amount = 150m, Currency = "ARS" });
            await db.SaveChangesAsync();
        }
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(2, result.Movements.Count);
        Assert.Contains(result.Movements, m => m.Source == MovementSource.BankDebit);
        Assert.Contains(result.Movements, m => m.Source == MovementSource.CreditCard);
    }

    [Fact]
    public async Task GenerateAsync_IntegracionReal_MovimientosYaClasificadosQuedanExcluidosDelResultado()
    {
        var dbName = Guid.NewGuid().ToString();
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        Guid transactionId;
        await using (var db = OpenDb(dbName))
        {
            var transaction = new Transaction { Date = date, Description = "Ya clasificada", Amount = 80m, Currency = "ARS" };
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();
            transactionId = transaction.Id;
        }
        await MarkAsClassifiedAsync(dbName, SourceEntityType.Transaction, transactionId);
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Empty(result.Movements);
        Assert.Empty(result.Suspicious);
    }

    [Fact]
    public async Task GenerateAsync_IntegracionReal_FechaEnElLimiteSuperiorDelPeriodo_QuedaIncluida()
    {
        var dbName = Guid.NewGuid().ToString();
        var lateOnLastDay = new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc);
        await using (var db = OpenDb(dbName))
        {
            db.BankStatements.Add(new BankStatement { Date = lateOnLastDay, Concept = "Movimiento tardio", Amount = 90m, Currency = "ARS", BankName = "BBVA" });
            await db.SaveChangesAsync();
        }
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Single(result.Movements);
    }

    // ── Caso especialmente importante: inversión de signo ────────────────────
    //
    // ReviewEngine no reprocesa ni reinterpreta el signo en ningún punto -- se limita
    // a devolver los FinancialMovement tal como los produjo MovementLoader. Estos tests
    // confirman que la inversión de signo documentada en
    // MovementLoader.ToFinancialMovement(BankStatement) (BankStatement: positivo =
    // crédito/ingreso; FinancialMovement: positivo = gasto/débito) llega intacta hasta
    // el ReviewResult final del pipeline completo, y que Transaction (que no se
    // invierte) también lo hace. Ver MovementLoaderTests para la cobertura dedicada de
    // esta regla a nivel del loader solo.

    [Fact]
    public async Task GenerateAsync_IntegracionReal_LaInversionDeSignoDelBankStatementSePropagaAlResultado()
    {
        var dbName = Guid.NewGuid().ToString();
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = OpenDb(dbName))
        {
            db.BankStatements.Add(new BankStatement { Date = date, Concept = "Transferencia recibida", Amount = 300m, Currency = "ARS", BankName = "BBVA" });
            await db.SaveChangesAsync();
        }
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var movement = Assert.Single(result.Movements);
        Assert.Equal(-300m, movement.Amount);
    }

    [Fact]
    public async Task GenerateAsync_IntegracionReal_TransactionMantieneElSignoSinInvertirEnElResultado()
    {
        var dbName = Guid.NewGuid().ToString();
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = OpenDb(dbName))
        {
            db.Transactions.Add(new Transaction { Date = date, Description = "Nota de crédito", Amount = -80m, Currency = "ARS" });
            await db.SaveChangesAsync();
        }
        var engine = CreateRealEngine(dbName);

        var result = await engine.GenerateAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var movement = Assert.Single(result.Movements);
        Assert.Equal(-80m, movement.Amount);
    }
}
