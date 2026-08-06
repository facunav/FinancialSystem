using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Planning.Commands;
using FinancialSystem.Domain.Planning;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Planning;

/// <summary>
/// Cobertura restante del módulo Planning (PATCH-040, Epic W) -- se verificó la
/// implementación real antes de escribir estos tests: existen 8 handlers de comando en
/// src/FinancialSystem.Application/Planning/Commands/. <see cref="CopyPlanningMonthHandler"/>
/// ya tenía cobertura propia (CopyPlanningMonthHandlerTests.cs, Patch 0043) y no se
/// repite acá; los 2 servicios de infraestructura del módulo
/// (<c>FinancialSystem.Infrastructure.Planning.PlanningQueryService</c> y
/// <c>FinancialSystem.Infrastructure.Planning.PlanningMatchSuggestionService</c>)
/// también ya tenían cobertura propia. Este archivo cubre los 7 handlers restantes:
/// CreatePlanningMonthHandler, AddPlanningItemHandler, EditPlanningItemHandler,
/// DeletePlanningItemHandler, MarkPlanningItemAsPaidHandler,
/// UnmarkPlanningItemAsPaidHandler, UpdateExpectedIncomeHandler.
///
/// PlanningMonth/PlanningItem no tienen ninguna relación con Category, Counterparty,
/// FinancialAccount ni con movimientos bancarios/tarjeta -- es una decisión de diseño
/// explícita del módulo (ver docs/Epics/Epica-PlanificacionMensual.md, sección 5), no
/// una omisión de estos tests. Por eso no hay casos de "categoría inexistente", "cuenta
/// inexistente" ni "movimiento relacionado": ese vocabulario, aplicable a otros módulos
/// (Clasificación, Auditoría), no tiene equivalente acá.
///
/// Mismo patrón de AppDbContext InMemory ya establecido en el resto de la suite (cada
/// paso lógico abre su propio contexto sobre el mismo nombre de base) y mismo patrón de
/// FakeDateTimeProvider ya usado en InvestigationsHandlerTests. Todos los handlers son
/// public sealed -- no requieren InternalsVisibleTo.
///
/// Ningún test acá modifica los handlers ni corrige comportamiento -- ver PATCH-040.
/// </summary>
public class PlanningHandlersTests
{
    private static readonly DateTime FixedNow = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DefaultPeriod = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => FixedNow;
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static CreatePlanningMonthHandler CreateCreateMonthHandler(string dbName) => new(OpenDb(dbName));
    private static AddPlanningItemHandler CreateAddItemHandler(string dbName) => new(OpenDb(dbName));
    private static EditPlanningItemHandler CreateEditItemHandler(string dbName) => new(OpenDb(dbName));
    private static DeletePlanningItemHandler CreateDeleteItemHandler(string dbName) => new(OpenDb(dbName));
    private static MarkPlanningItemAsPaidHandler CreateMarkPaidHandler(string dbName) => new(OpenDb(dbName), new FakeDateTimeProvider());
    private static UnmarkPlanningItemAsPaidHandler CreateUnmarkPaidHandler(string dbName) => new(OpenDb(dbName));
    private static UpdateExpectedIncomeHandler CreateUpdateIncomeHandler(string dbName) => new(OpenDb(dbName));

    private static async Task<Guid> SeedMonthAsync(
        string dbName, DateTime? period = null, decimal? expectedIncome = null)
    {
        await using var db = OpenDb(dbName);
        var month = new PlanningMonth { Period = period ?? DefaultPeriod, ExpectedIncome = expectedIncome };
        db.PlanningMonths.Add(month);
        await db.SaveChangesAsync();
        return month.Id;
    }

    private static async Task<Guid> SeedItemAsync(
        string dbName,
        Guid planningMonthId,
        string title = "Item de prueba",
        decimal? expectedAmount = 100m,
        DateTime? dueDate = null,
        bool isPaid = false,
        DateTime? paidAt = null)
    {
        await using var db = OpenDb(dbName);
        var item = new PlanningItem
        {
            PlanningMonthId = planningMonthId,
            Title = title,
            ExpectedAmount = expectedAmount,
            DueDate = dueDate,
            IsPaid = isPaid,
            PaidAt = paidAt,
        };
        db.PlanningItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreatePlanningMonthHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_PeriodoNuevo_CreaElPlanningMonth()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateMonthHandler(dbName).Handle(new CreatePlanningMonthCommand(DefaultPeriod));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.PlanningMonthId);
        Assert.Equal(DefaultPeriod, result.Period);
    }

    [Fact]
    public async Task Create_NormalizaElPeriodoAlPrimerDiaDelMesUtc()
    {
        var dbName = Guid.NewGuid().ToString();
        var messyPeriod = new DateTime(2026, 3, 15, 14, 30, 0, DateTimeKind.Utc);

        var result = await CreateCreateMonthHandler(dbName).Handle(new CreatePlanningMonthCommand(messyPeriod));

        Assert.True(result.IsSuccess);
        Assert.Equal(DefaultPeriod, result.Period);

        await using var db = OpenDb(dbName);
        Assert.Equal(DefaultPeriod, (await db.PlanningMonths.SingleAsync()).Period);
    }

    [Fact]
    public async Task Create_PeriodoYaExistente_DevuelvePeriodAlreadyExistsSinCrearOtro()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedMonthAsync(dbName, period: DefaultPeriod);

        // Mismo mes, distinto dia/hora -- normaliza al mismo periodo ya existente.
        var result = await CreateCreateMonthHandler(dbName).Handle(
            new CreatePlanningMonthCommand(new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc)));

        Assert.False(result.IsSuccess);
        Assert.Equal(CreatePlanningMonthFailureReason.PeriodAlreadyExists, result.FailureReason);

        await using var db = OpenDb(dbName);
        Assert.Equal(1, await db.PlanningMonths.CountAsync());
    }

    [Fact]
    public async Task Create_NaceSinExpectedIncomeYSinItems()
    {
        var dbName = Guid.NewGuid().ToString();

        await CreateCreateMonthHandler(dbName).Handle(new CreatePlanningMonthCommand(DefaultPeriod));

        await using var db = OpenDb(dbName);
        var month = await db.PlanningMonths.Include(m => m.Items).SingleAsync();
        Assert.Null(month.ExpectedIncome);
        Assert.Empty(month.Items);
    }

    [Fact]
    public async Task Create_MesesConDistintosPeriodos_SePuedenCrearAmbos()
    {
        var dbName = Guid.NewGuid().ToString();

        var first = await CreateCreateMonthHandler(dbName).Handle(new CreatePlanningMonthCommand(DefaultPeriod));
        var second = await CreateCreateMonthHandler(dbName).Handle(
            new CreatePlanningMonthCommand(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.PlanningMonthId, second.PlanningMonthId);

        await using var db = OpenDb(dbName);
        Assert.Equal(2, await db.PlanningMonths.CountAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AddPlanningItemHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddItem_PlanningMonthExistente_CreaElItemEnEstadoPendingConDatosCompletos()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var dueDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        var result = await CreateAddItemHandler(dbName).Handle(
            new AddPlanningItemCommand(monthId, "Internet", 150m, dueDate));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.PlanningItemId);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.Equal(monthId, item.PlanningMonthId);
        Assert.Equal("Internet", item.Title);
        Assert.Equal(150m, item.ExpectedAmount);
        Assert.Equal(dueDate, item.DueDate);
        Assert.False(item.IsPaid);
        Assert.Null(item.PaidAt);
    }

    [Fact]
    public async Task AddItem_DatosMinimos_SinMontoNiFecha_SeAceptaSinFallar()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);

        var result = await CreateAddItemHandler(dbName).Handle(
            new AddPlanningItemCommand(monthId, "Concepto pendiente de monto", null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.Null(item.ExpectedAmount);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public async Task AddItem_PlanningMonthInexistente_DevuelvePlanningMonthNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateAddItemHandler(dbName).Handle(
            new AddPlanningItemCommand(Guid.NewGuid(), "Item", 100m, null));

        Assert.False(result.IsSuccess);
        Assert.Null(result.PlanningItemId);

        await using var db = OpenDb(dbName);
        Assert.Empty(await db.PlanningItems.ToListAsync());
    }

    [Fact]
    public async Task AddItem_MultiplesItemsParaElMismoMes_SeAgreganTodosIndependientemente()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);

        await CreateAddItemHandler(dbName).Handle(new AddPlanningItemCommand(monthId, "Internet", 150m, null));
        await CreateAddItemHandler(dbName).Handle(new AddPlanningItemCommand(monthId, "Netflix", 20m, null));

        await using var db = OpenDb(dbName);
        var items = await db.PlanningItems.Where(i => i.PlanningMonthId == monthId).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Title == "Internet");
        Assert.Contains(items, i => i.Title == "Netflix");
    }

    [Fact]
    public async Task AddItem_TituloVacio_SeAceptaSinValidar()
    {
        // Comportamiento actual documentado tal cual: el handler no valida Title --
        // ni longitud minima ni contenido no vacio.
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);

        var result = await CreateAddItemHandler(dbName).Handle(
            new AddPlanningItemCommand(monthId, string.Empty, null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Equal(string.Empty, (await db.PlanningItems.SingleAsync()).Title);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EditPlanningItemHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edit_ItemExistente_ActualizaTituloMontoYFecha()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(
            dbName, monthId, title: "Internet", expectedAmount: 100m,
            dueDate: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));
        var newDueDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        var result = await CreateEditItemHandler(dbName).Handle(
            new EditPlanningItemCommand(itemId, "Internet + cable", 180m, newDueDate));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.Equal("Internet + cable", item.Title);
        Assert.Equal(180m, item.ExpectedAmount);
        Assert.Equal(newDueDate, item.DueDate);
    }

    [Fact]
    public async Task Edit_ItemInexistente_DevuelveNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateEditItemHandler(dbName).Handle(
            new EditPlanningItemCommand(Guid.NewGuid(), "Titulo", null, null));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Edit_PasandoMontoYFechaEnNull_LosLimpia()
    {
        // Comportamiento actual documentado tal cual, no corregido: EditPlanningItemHandler
        // sobrescribe siempre los 3 campos recibidos -- no existe un modo de
        // actualizacion parcial que preserve el valor anterior cuando se omite un
        // campo. Pasar null en ExpectedAmount/DueDate los limpia, aunque antes
        // tuvieran un valor.
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(
            dbName, monthId, expectedAmount: 200m, dueDate: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateEditItemHandler(dbName).Handle(
            new EditPlanningItemCommand(itemId, "Titulo nuevo", null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.Null(item.ExpectedAmount);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public async Task Edit_NoModificaIsPaidNiPaidAt()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var paidAt = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc);
        var itemId = await SeedItemAsync(dbName, monthId, isPaid: true, paidAt: paidAt);

        await CreateEditItemHandler(dbName).Handle(new EditPlanningItemCommand(itemId, "Titulo nuevo", 50m, null));

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.True(item.IsPaid);
        Assert.Equal(paidAt, item.PaidAt);
    }

    [Fact]
    public async Task Edit_NoAfectaOtrosItemsDelMismoMes()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId1 = await SeedItemAsync(dbName, monthId, title: "Item 1");
        var itemId2 = await SeedItemAsync(dbName, monthId, title: "Item 2");

        await CreateEditItemHandler(dbName).Handle(new EditPlanningItemCommand(itemId1, "Item 1 editado", null, null));

        await using var db = OpenDb(dbName);
        var item2 = await db.PlanningItems.SingleAsync(i => i.Id == itemId2);
        Assert.Equal("Item 2", item2.Title);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DeletePlanningItemHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_ItemExistente_LoElimina()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(dbName, monthId);

        var result = await CreateDeleteItemHandler(dbName).Handle(new DeletePlanningItemCommand(itemId));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Empty(await db.PlanningItems.ToListAsync());
    }

    [Fact]
    public async Task Delete_ItemInexistente_DevuelveNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateDeleteItemHandler(dbName).Handle(new DeletePlanningItemCommand(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Delete_EliminacionRepetida_LaSegundaVezDevuelveNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(dbName, monthId);

        var first = await CreateDeleteItemHandler(dbName).Handle(new DeletePlanningItemCommand(itemId));
        var second = await CreateDeleteItemHandler(dbName).Handle(new DeletePlanningItemCommand(itemId));

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task Delete_SoloAfectaAlItemIndicado_ElMesYOtrosItemsQuedanIntactos()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemToDelete = await SeedItemAsync(dbName, monthId, title: "Se borra");
        var itemToKeep = await SeedItemAsync(dbName, monthId, title: "Se mantiene");

        await CreateDeleteItemHandler(dbName).Handle(new DeletePlanningItemCommand(itemToDelete));

        await using var db = OpenDb(dbName);
        Assert.NotNull(await db.PlanningMonths.FindAsync(monthId));
        var remaining = await db.PlanningItems.SingleAsync();
        Assert.Equal(itemToKeep, remaining.Id);
    }

    [Fact]
    public async Task Delete_UltimoItemDelMes_ElMesQuedaSinItemsPeroSigueExistiendo()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(dbName, monthId);

        await CreateDeleteItemHandler(dbName).Handle(new DeletePlanningItemCommand(itemId));

        await using var db = OpenDb(dbName);
        var month = await db.PlanningMonths.Include(m => m.Items).SingleAsync();
        Assert.Equal(monthId, month.Id);
        Assert.Empty(month.Items);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MarkPlanningItemAsPaidHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsPaid_ItemPendiente_QuedaPagadoConPaidAtDelDateTimeProvider()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(dbName, monthId, isPaid: false, paidAt: null);

        var result = await CreateMarkPaidHandler(dbName).Handle(new MarkPlanningItemAsPaidCommand(itemId));

        Assert.True(result.IsSuccess);
        Assert.Equal(FixedNow, result.PaidAt);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.True(item.IsPaid);
        Assert.Equal(FixedNow, item.PaidAt);
    }

    [Fact]
    public async Task MarkAsPaid_ItemInexistente_DevuelveNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateMarkPaidHandler(dbName).Handle(new MarkPlanningItemAsPaidCommand(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Null(result.PaidAt);
    }

    [Fact]
    public async Task MarkAsPaid_ItemYaPagado_ReescribePaidAtConElNuevoValorDelReloj()
    {
        // Comportamiento actual documentado tal cual, no corregido: el handler no
        // verifica el estado actual de IsPaid antes de actuar -- volver a marcar un
        // item ya pagado simplemente reescribe PaidAt con el valor actual del reloj,
        // sin fallar ni conservar el PaidAt original.
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var originalPaidAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var itemId = await SeedItemAsync(dbName, monthId, isPaid: true, paidAt: originalPaidAt);

        var result = await CreateMarkPaidHandler(dbName).Handle(new MarkPlanningItemAsPaidCommand(itemId));

        Assert.True(result.IsSuccess);
        Assert.Equal(FixedNow, result.PaidAt);

        await using var db = OpenDb(dbName);
        Assert.Equal(FixedNow, (await db.PlanningItems.SingleAsync()).PaidAt);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UnmarkPlanningItemAsPaidHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Unmark_ItemPagado_VuelveAPending()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(
            dbName, monthId, isPaid: true, paidAt: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateUnmarkPaidHandler(dbName).Handle(new UnmarkPlanningItemAsPaidCommand(itemId));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.False(item.IsPaid);
        Assert.Null(item.PaidAt);
    }

    [Fact]
    public async Task Unmark_ItemInexistente_DevuelveNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateUnmarkPaidHandler(dbName).Handle(new UnmarkPlanningItemAsPaidCommand(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Unmark_ItemYaPendiente_NoFallaYQuedaIgual()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName);
        var itemId = await SeedItemAsync(dbName, monthId, isPaid: false, paidAt: null);

        var result = await CreateUnmarkPaidHandler(dbName).Handle(new UnmarkPlanningItemAsPaidCommand(itemId));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var item = await db.PlanningItems.SingleAsync();
        Assert.False(item.IsPaid);
        Assert.Null(item.PaidAt);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateExpectedIncomeHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateIncome_MesExistente_ActualizaElValor()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName, expectedIncome: null);

        var result = await CreateUpdateIncomeHandler(dbName).Handle(new UpdateExpectedIncomeCommand(monthId, 500000m));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Equal(500000m, (await db.PlanningMonths.SingleAsync()).ExpectedIncome);
    }

    [Fact]
    public async Task UpdateIncome_MesInexistente_DevuelveNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateUpdateIncomeHandler(dbName).Handle(new UpdateExpectedIncomeCommand(Guid.NewGuid(), 100m));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateIncome_ValorNull_LimpiaElIngresoEsperado()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName, expectedIncome: 300000m);

        var result = await CreateUpdateIncomeHandler(dbName).Handle(new UpdateExpectedIncomeCommand(monthId, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Null((await db.PlanningMonths.SingleAsync()).ExpectedIncome);
    }

    [Fact]
    public async Task UpdateIncome_SobrescribeUnValorPrevio()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName, expectedIncome: 100000m);

        await CreateUpdateIncomeHandler(dbName).Handle(new UpdateExpectedIncomeCommand(monthId, 200000m));

        await using var db = OpenDb(dbName);
        Assert.Equal(200000m, (await db.PlanningMonths.SingleAsync()).ExpectedIncome);
    }
}
