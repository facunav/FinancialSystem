using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Movements;

internal sealed class MovementLookupService : IMovementLookupService
{
    private readonly IApplicationDbContext _db;

    public MovementLookupService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<MovementDetail?> GetBySourceAsync(
        SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var baseDetail = sourceEntityType switch
        {
            SourceEntityType.Transaction => await LoadTransactionAsync(sourceId, cancellationToken),
            SourceEntityType.BankStatement => await LoadBankStatementAsync(sourceId, cancellationToken),
            // PR-L5: LegacyImportedExpense se eliminó -- no queda tabla de origen detrás de
            // SourceEntityType.LegacyImport (mismo caso ya documentado en
            // ClassifyMovementHandler.FindSourceAsync).
            _ => null,
        };

        if (baseDetail is null) return null;

        var classification = await LoadClassificationAsync(sourceEntityType, sourceId, cancellationToken);
        return baseDetail with { Classification = classification };
    }

    // PATCH-044: versión en lote de GetBySourceAsync -- ver doc-comment en
    // IMovementLookupService. Reemplaza el patrón "N sources -> hasta 5*N consultas"
    // (un GetBySourceAsync por elemento, cada uno con su propia consulta de
    // Transaction/BankStatement + FinancialAccount + ClassifiedMovementItem + Category
    // + Counterparty) por una cantidad fija de consultas -- una por tabla
    // (Transactions, BankStatements, FinancialAccounts, ClassifiedMovementItems,
    // Categories, Counterparties), sin importar cuántos elementos traiga `sources`.
    //
    // Reutiliza los mismos mappers puros (ToMovementDetail/BuildClassificationDetail)
    // que ya usa el camino individual arriba -- misma forma de MovementDetail/
    // MovementClassificationDetail, mismos campos, mismos criterios de null. No hay dos
    // implementaciones del mapeo: sólo dos formas de resolver los datos de entrada
    // (una consulta por Id vs. varias consultas por lote).
    public async Task<IReadOnlyDictionary<(SourceEntityType SourceEntityType, Guid SourceId), MovementDetail>> GetManyBySourceAsync(
        IReadOnlyList<(SourceEntityType SourceEntityType, Guid SourceId)> sources,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<(SourceEntityType, Guid), MovementDetail>();
        if (sources.Count == 0) return results;

        var transactionIds = sources
            .Where(s => s.SourceEntityType == SourceEntityType.Transaction)
            .Select(s => s.SourceId)
            .Distinct()
            .ToList();
        var bankStatementIds = sources
            .Where(s => s.SourceEntityType == SourceEntityType.BankStatement)
            .Select(s => s.SourceId)
            .Distinct()
            .ToList();

        var transactions = transactionIds.Count == 0
            ? new List<Transaction>()
            : await _db.Transactions.AsNoTracking().Where(t => transactionIds.Contains(t.Id)).ToListAsync(cancellationToken);
        var bankStatements = bankStatementIds.Count == 0
            ? new List<BankStatement>()
            : await _db.BankStatements.AsNoTracking().Where(b => bankStatementIds.Contains(b.Id)).ToListAsync(cancellationToken);

        var accountIds = transactions
            .Where(t => t.FinancialAccountId is not null)
            .Select(t => t.FinancialAccountId!.Value)
            .Concat(bankStatements.Where(b => b.FinancialAccountId is not null).Select(b => b.FinancialAccountId!.Value))
            .Distinct()
            .ToList();
        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.FinancialAccounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        // Patch 0107: mismo criterio que accountNames -- UNA consulta IN para todo el lote
        // (nunca una por movimiento), contra IApplicationDbContext.ImportBatches. Los
        // ImportBatchId ausentes del diccionario resultante (porque importBatchId era null,
        // o porque el ImportBatch referenciado no existe) se resuelven a null más abajo,
        // igual que ResolveImportOriginAsync en el camino individual.
        var importBatchIds = transactions
            .Where(t => t.ImportBatchId is not null)
            .Select(t => t.ImportBatchId!.Value)
            .Concat(bankStatements.Where(b => b.ImportBatchId is not null).Select(b => b.ImportBatchId!.Value))
            .Distinct()
            .ToList();
        var importOrigins = importBatchIds.Count == 0
            ? new Dictionary<Guid, MovementImportOrigin>()
            : await _db.ImportBatches.AsNoTracking()
                .Where(b => importBatchIds.Contains(b.Id))
                .Select(b => new MovementImportOrigin(b.Id, b.SourceFile, b.HandlerName, b.ParserUsed, b.StartedAtUtc, b.Outcome))
                .ToDictionaryAsync(o => o.ImportBatchId, cancellationToken);

        // Mismo predicado que LoadClassificationAsync (SourceEntityType + SourceId),
        // acotado a los dos lotes de Ids ya resueltos arriba -- no trae toda la tabla.
        //
        // Patch 0108: sin ThenInclude(cm => cm!.Items) -- ver el mismo comentario en
        // LoadClassificationAsync. El grupo de matching de cada ClassifiedMovement se
        // resuelve aparte, en una única consulta batched (groupItemsByMovement, abajo),
        // en vez de por la navegación cíclica.
        var classificationItems = transactionIds.Count == 0 && bankStatementIds.Count == 0
            ? new List<ClassifiedMovementItem>()
            : await _db.ClassifiedMovementItems
                .AsNoTracking()
                .Include(i => i.ClassifiedMovement)
                .Where(i =>
                    (i.SourceEntityType == SourceEntityType.Transaction && transactionIds.Contains(i.SourceId)) ||
                    (i.SourceEntityType == SourceEntityType.BankStatement && bankStatementIds.Contains(i.SourceId)))
                .ToListAsync(cancellationToken);

        // GroupBy + First (no ToDictionary directo) por la misma razón que
        // LoadClassificationAsync usa FirstOrDefaultAsync en vez de SingleOrDefaultAsync:
        // (SourceEntityType, SourceId) debería ser único en ClassifiedMovementItems (así
        // lo asume ClassifyMovementHandler al buscar el existente antes de crear uno
        // nuevo), pero esta versión en lote no depende de esa garantía para no fallar
        // con una excepción donde el camino individual no fallaría.
        var classificationsBySource = classificationItems
            .GroupBy(i => (i.SourceEntityType, i.SourceId))
            .ToDictionary(g => g.Key, g => g.First());

        // Patch 0108: grupo de matching de cada ClassifiedMovement (ClassifiedMovementItem.Role
        // por cada ítem hermano) resuelto en UNA consulta batched más -- sigue siendo una
        // cantidad fija de consultas para todo el lote, sin importar cuántos `sources` haya,
        // igual que accountNames/importOrigins/categoryNames/counterpartyNames arriba.
        var classifiedMovementIds = classificationItems
            .Select(i => i.ClassifiedMovementId)
            .Distinct()
            .ToList();
        var groupItemsByMovement = classifiedMovementIds.Count == 0
            ? new Dictionary<Guid, List<ClassifiedMovementItem>>()
            : (await _db.ClassifiedMovementItems
                .AsNoTracking()
                .Where(i => classifiedMovementIds.Contains(i.ClassifiedMovementId))
                .ToListAsync(cancellationToken))
                .GroupBy(i => i.ClassifiedMovementId)
                .ToDictionary(g => g.Key, g => g.ToList());

        var categoryIds = classificationItems.Select(i => i.ClassifiedMovement!.CategoryId).Distinct().ToList();
        var categoryNames = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.DisplayName, cancellationToken);

        var counterpartyIds = classificationItems
            .Where(i => i.ClassifiedMovement!.CounterpartyId is not null)
            .Select(i => i.ClassifiedMovement!.CounterpartyId!.Value)
            .Distinct()
            .ToList();
        var counterpartyNames = counterpartyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Counterparties.AsNoTracking()
                .Where(c => counterpartyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        foreach (var t in transactions)
        {
            var accountName = t.FinancialAccountId is { } accId ? accountNames.GetValueOrDefault(accId) : null;
            var importOrigin = t.ImportBatchId is { } batchId ? importOrigins.GetValueOrDefault(batchId) : null;
            var classification = ResolveClassification(
                classificationsBySource, SourceEntityType.Transaction, t.Id, categoryNames, counterpartyNames, groupItemsByMovement);
            results[(SourceEntityType.Transaction, t.Id)] = ToMovementDetail(t, accountName, importOrigin) with { Classification = classification };
        }

        foreach (var b in bankStatements)
        {
            var accountName = b.FinancialAccountId is { } accId ? accountNames.GetValueOrDefault(accId) : null;
            var importOrigin = b.ImportBatchId is { } batchId ? importOrigins.GetValueOrDefault(batchId) : null;
            var classification = ResolveClassification(
                classificationsBySource, SourceEntityType.BankStatement, b.Id, categoryNames, counterpartyNames, groupItemsByMovement);
            results[(SourceEntityType.BankStatement, b.Id)] = ToMovementDetail(b, accountName, importOrigin) with { Classification = classification };
        }

        return results;
    }

    private async Task<MovementDetail?> LoadTransactionAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;

        var accountName = await ResolveFinancialAccountNameAsync(t.FinancialAccountId, ct);
        var importOrigin = await ResolveImportOriginAsync(t.ImportBatchId, ct);
        return ToMovementDetail(t, accountName, importOrigin);
    }

    private async Task<MovementDetail?> LoadBankStatementAsync(Guid id, CancellationToken ct)
    {
        var b = await _db.BankStatements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return null;

        var accountName = await ResolveFinancialAccountNameAsync(b.FinancialAccountId, ct);
        var importOrigin = await ResolveImportOriginAsync(b.ImportBatchId, ct);
        return ToMovementDetail(b, accountName, importOrigin);
    }

    private static MovementDetail ToMovementDetail(Transaction t, string? accountName, MovementImportOrigin? importOrigin) => new(
        SourceEntityType.Transaction, t.Id, t.Date, t.Description, t.Amount, t.Currency,
        SourceFile: t.SourceFile,
        FinancialAccountId: t.FinancialAccountId,
        FinancialAccountName: accountName,
        ExternalId: t.ExternalId,
        CouponNumber: t.CouponNumber,
        RawLine: t.RawLine,
        BankName: null,
        AccountNumber: null,
        BankDetail: null,
        Balance: null,
        SheetName: null,
        RowNumber: null,
        Merchant: null,
        MerchantAtUtc: null,
        SourceRecordedAtUtc: t.CreatedAtUtc,
        Classification: null,
        ImportBatchId: t.ImportBatchId,
        ImportOrigin: importOrigin);

    private static MovementDetail ToMovementDetail(BankStatement b, string? accountName, MovementImportOrigin? importOrigin) => new(
        SourceEntityType.BankStatement, b.Id, b.Date, b.Concept, b.Amount, b.Currency,
        SourceFile: b.SourceFile,
        FinancialAccountId: b.FinancialAccountId,
        FinancialAccountName: accountName,
        ExternalId: b.ExternalId,
        CouponNumber: null,
        RawLine: null,
        BankName: b.BankName,
        AccountNumber: b.AccountNumber,
        BankDetail: b.Detail,
        Balance: b.Balance,
        SheetName: b.SheetName,
        RowNumber: b.RowNumber,
        Merchant: b.Merchant,
        MerchantAtUtc: b.MerchantAtUtc,
        SourceRecordedAtUtc: b.ImportedAtUtc,
        Classification: null,
        ImportBatchId: b.ImportBatchId,
        ImportOrigin: importOrigin);

    private async Task<string?> ResolveFinancialAccountNameAsync(Guid? accountId, CancellationToken ct)
    {
        if (accountId is not { } id) return null;

        return await _db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(ct);
    }

    // Patch 0107: mismo criterio que ResolveFinancialAccountNameAsync -- una consulta puntual
    // por Id contra IApplicationDbContext.ImportBatches (ya expuesto, sin cambios de
    // interfaz), sin FK/navegación (ImportBatchId sigue siendo referencia blanda, Patch 0105).
    // Null tanto si importBatchId es null (movimiento sin corrida registrada) como si el
    // ImportBatch referenciado no existe (referencia blanda colgante) -- el llamador
    // distingue esos dos casos comparando contra el ImportBatchId crudo que ya tiene
    // (MovementDetail.ImportBatchId), no responsabilidad de este método.
    private async Task<MovementImportOrigin?> ResolveImportOriginAsync(Guid? importBatchId, CancellationToken ct)
    {
        if (importBatchId is not { } id) return null;

        return await _db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new MovementImportOrigin(b.Id, b.SourceFile, b.HandlerName, b.ParserUsed, b.StartedAtUtc, b.Outcome))
            .FirstOrDefaultAsync(ct);
    }

    // Patch 0108 -- FIX EF Core "Cycles are not allowed in no-tracking queries":
    //
    // Antes, esta consulta hacía .Include(i => i.ClassifiedMovement).ThenInclude(cm =>
    // cm!.Items) -- Item -> ClassifiedMovement -> Items vuelve a incluir el propio tipo
    // ClassifiedMovementItem (cm.Items contiene, entre otros, al mismo `item` del que
    // partimos). Para no-tracking, EF Core exige poder resolver esa identidad repetida
    // sin un change tracker, y lo rechaza con InvalidOperationException ("Cycles are not
    // allowed in no-tracking queries") -- no es un problema de datos (pasa incluso con
    // ClassifiedMovementItems vacía): es la FORMA del Include, siempre. No es
    // particular del proveedor InMemory: la verificación vive en
    // Microsoft.EntityFrameworkCore.Query.Internal (ensamblado core), no en
    // Microsoft.EntityFrameworkCore.InMemory -- reproduce igual contra Postgres.
    //
    // Se evaluó AsNoTrackingWithIdentityResolution() (resolvería el ciclo manteniendo un
    // mapa de identidad efímero) pero se descartó: cambia el costo de memoria por
    // consulta de forma menos predecible que separar la consulta, y no está probado en
    // ningún otro punto del proyecto (grep sin resultados) -- se prefirió no introducir
    // un modo de tracking nuevo para todo el proyecto por este único caso.
    //
    // FIX: se elimina el ThenInclude (Include(i => i.ClassifiedMovement) solo, sin
    // ciclo -- Item -> Movement es una línea recta) y el grupo de matching (antes
    // cm.Items) se resuelve en una segunda consulta explícita, sin Include, filtrando
    // por ClassifiedMovementId -- mismo criterio que ya usa esta clase para
    // FinancialAccountName/ImportOrigin (una consulta puntual más, no una por
    // movimiento). Mismos datos exactos que antes: BuildClassificationDetail sigue
    // devolviendo el mismo MatchGroupItem por cada ítem del grupo -- ningún consumidor
    // (GetMovement/ExplainMovement/ExplainClassification/InvestigationTools) ve un
    // MovementClassificationDetail distinto al de antes del fix.
    private async Task<MovementClassificationDetail?> LoadClassificationAsync(
        SourceEntityType sourceEntityType, Guid sourceId, CancellationToken ct)
    {
        var item = await _db.ClassifiedMovementItems
            .AsNoTracking()
            .Include(i => i.ClassifiedMovement)
            .FirstOrDefaultAsync(
                i => i.SourceEntityType == sourceEntityType && i.SourceId == sourceId, ct);

        if (item is null) return null;

        var cm = item.ClassifiedMovement!;

        var groupItemEntities = await _db.ClassifiedMovementItems
            .AsNoTracking()
            .Where(i => i.ClassifiedMovementId == cm.Id)
            .ToListAsync(ct);

        var categoryName = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Id == cm.CategoryId)
            .Select(c => c.DisplayName)
            .FirstOrDefaultAsync(ct);

        var counterpartyName = cm.CounterpartyId is { } counterpartyId
            ? await _db.Counterparties
                .AsNoTracking()
                .Where(c => c.Id == counterpartyId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        return BuildClassificationDetail(item, categoryName, counterpartyName, groupItemEntities);
    }

    private static MovementClassificationDetail? ResolveClassification(
        IReadOnlyDictionary<(SourceEntityType, Guid), ClassifiedMovementItem> classificationsBySource,
        SourceEntityType sourceEntityType,
        Guid sourceId,
        IReadOnlyDictionary<Guid, string> categoryNames,
        IReadOnlyDictionary<Guid, string> counterpartyNames,
        IReadOnlyDictionary<Guid, List<ClassifiedMovementItem>> groupItemsByMovement)
    {
        if (!classificationsBySource.TryGetValue((sourceEntityType, sourceId), out var item)) return null;

        var cm = item.ClassifiedMovement!;
        var categoryName = categoryNames.GetValueOrDefault(cm.CategoryId);
        var counterpartyName = cm.CounterpartyId is { } cpId ? counterpartyNames.GetValueOrDefault(cpId) : null;
        var groupItemEntities = groupItemsByMovement.GetValueOrDefault(cm.Id) ?? [];

        return BuildClassificationDetail(item, categoryName, counterpartyName, groupItemEntities);
    }

    // Patch 0108: groupItemEntities reemplaza a cm.Items (ver LoadClassificationAsync) --
    // mismo mapeo a MatchGroupItem, ninguna otra diferencia.
    private static MovementClassificationDetail BuildClassificationDetail(
        ClassifiedMovementItem item, string? categoryName, string? counterpartyName,
        IReadOnlyList<ClassifiedMovementItem> groupItemEntities)
    {
        var cm = item.ClassifiedMovement!;

        var groupItems = groupItemEntities
            .Select(i => new MatchGroupItem(i.SourceEntityType, i.SourceId, i.Role))
            .ToList();

        return new MovementClassificationDetail(
            cm.Id, cm.EffectiveDate, cm.CategoryId, categoryName, cm.CounterpartyId, counterpartyName,
            cm.MovementType, cm.FinancialImpact, cm.Status, cm.ProcessingSource, cm.Comment,
            cm.MatchScore, cm.AmountDelta, cm.CreatedAt, cm.ProcessedAt, cm.ProcessedBy,
            item.Role, groupItems);
    }
}
