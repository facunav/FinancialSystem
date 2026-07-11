using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Suggestions;

/// <summary>
/// Implementación real de <see cref="IClassificationSuggestionService"/>, con dos
/// heurísticas determinísticas — sin similitud difusa, sin IA. Completamente de solo
/// lectura: nunca escribe nada.
///
/// 1. (PR-S3) Exact match de descripción normalizada contra el historial de
///    <c>ClassifiedMovement</c> — ver <see cref="BuildSuggestions"/>.
/// 2. (PR-S7) Enriquecimiento vía <c>Counterparty.Default*</c>: cuando la heurística 1
///    ya sugirió una <c>Counterparty</c> para un movimiento, y esa contraparte tiene
///    <c>DefaultCategoryId</c>/<c>DefaultMovementType</c>/<c>DefaultFinancialImpact</c>
///    configurados, esos valores compiten como sugerencias adicionales — ver
///    <see cref="EnrichWithCounterpartyDefaultsAsync"/>. No es una regla independiente:
///    depende de que la heurística 1 ya haya resuelto una contraparte candidata, no hay
///    ninguna otra forma hoy de proponer una contraparte para un movimiento pendiente.
///
/// Deliberadamente NO se introduce ninguna abstracción de "regla" (ver análisis de
/// PR-S6, sección 5): con solo dos heurísticas y sin evidencia de que futuras reglas
/// compartan una forma común, extraer una interfaz ahora sería abstracción prematura.
/// Cada heurística es un método privado de esta clase.
///
/// Por qué la heurística 1 no consulta <c>ClassifiedMovementItem</c>/<c>Category</c>/
/// <c>Counterparty</c> directamente, aunque son fuentes de datos habilitadas para este
/// motor: <c>ClassifiedMovement.Description</c> ya es la misma descripción original que
/// <c>ClassifiedMovementItem.OriginalDescription</c> (ambas se copian del mismo
/// <c>FinancialMovement.Description</c> al momento de clasificar, ver
/// <c>ClassifyMovementHandler</c>) — unirse a <c>Items</c> no aportaría una segunda
/// descripción distinta para esa regla. Y el contrato de <see cref="ClassificationSuggestion"/>
/// sugiere el <see cref="Guid"/> de la categoría/contraparte, no la entidad completa —
/// no hace falta cargar <c>Category</c> para eso, igual que <c>MovementView</c> ya
/// expone <c>CategoryId</c>/<c>CounterpartyId</c> sin hidratar la entidad. La heurística 2
/// sí necesita consultar <c>Counterparty</c> (para leer sus valores por defecto), pero
/// en una sola consulta por lote — ver <see cref="EnrichWithCounterpartyDefaultsAsync"/>.
/// </summary>
internal sealed class ClassificationSuggestionService : IClassificationSuggestionService
{
    private readonly IApplicationDbContext _db;

    public ClassificationSuggestionService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
        IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default)
    {
        if (movements.Count == 0) return [];

        var normalizedDescriptions = movements
            .Select(m => Normalize(m.Description))
            .Where(d => d.Length > 0)
            .ToHashSet();

        if (normalizedDescriptions.Count == 0) return [];

        // Una sola consulta para todo el lote, sin importar cuántos movimientos ni
        // cuántas descripciones distintas tengan — evita tanto N+1 (una consulta por
        // movimiento) como una consulta por descripción distinta. ClassifiedMovement no
        // tiene índice sobre Description (confirmado en el análisis de PR-S1), y la
        // normalización es una función de C#, no traducible a SQL sin duplicar esa lógica
        // en la query — para el volumen de un sistema personal de un solo usuario, traer
        // el historial completo una vez y agrupar en memoria es la opción simple y
        // correcta. Si el volumen de ClassifiedMovements crece lo suficiente para que esto
        // deje de ser barato, un PR futuro puede agregar una columna normalizada indexada
        // y filtrar en la base — decisión a tomar con datos reales, no de antemano.
        var history = await _db.ClassifiedMovements
            .AsNoTracking()
            .Select(cm => new ClassifiedHistoryRow(
                cm.Description,
                cm.CategoryId,
                cm.MovementType,
                cm.FinancialImpact,
                cm.CounterpartyId,
                cm.ProcessedAt))
            .ToListAsync(cancellationToken);

        var historyByDescription = history
            .GroupBy(h => Normalize(h.Description))
            .Where(g => g.Key.Length > 0 && normalizedDescriptions.Contains(g.Key))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ClassifiedHistoryRow>)g.ToList());

        if (historyByDescription.Count == 0) return [];

        var results = new List<ClassificationSuggestionSet>();

        foreach (var movement in movements)
        {
            var key = Normalize(movement.Description);
            if (!historyByDescription.TryGetValue(key, out var matches)) continue;

            var suggestions = BuildSuggestions(matches);
            if (suggestions.Count == 0) continue;

            results.Add(new ClassificationSuggestionSet(
                ToSourceEntityType(movement.Source), movement.SourceId, suggestions));
        }

        if (results.Count == 0) return results;

        return await EnrichWithCounterpartyDefaultsAsync(results, cancellationToken);
    }

    /// <summary>
    /// PR-S7, heurística 2: para cada <see cref="ClassificationSuggestionSet"/> ya
    /// armado por la heurística 1, si tiene una sugerencia de <see cref="SuggestionDimension.Counterparty"/>,
    /// resuelve esa contraparte y, si tiene valores por defecto configurados, los agrega
    /// como sugerencias adicionales de Categoría/Tipo/Impacto — o reemplazan a las que ya
    /// había si tienen mayor confianza (ver <see cref="MergeDimension"/>).
    ///
    /// Una sola consulta para todo el lote — mismo criterio de PR-S3: se juntan todas
    /// las contrapartes candidatas de todos los movimientos del lote primero, y recién
    /// ahí se consulta una vez, nunca una consulta por movimiento. Si ningún movimiento
    /// tiene sugerencia de contraparte, no se ejecuta ninguna consulta adicional.
    /// </summary>
    private async Task<IReadOnlyList<ClassificationSuggestionSet>> EnrichWithCounterpartyDefaultsAsync(
        IReadOnlyList<ClassificationSuggestionSet> results, CancellationToken cancellationToken)
    {
        var suggestedCounterpartyIds = results
            .SelectMany(r => r.Suggestions)
            .Where(s => s.Dimension == SuggestionDimension.Counterparty)
            .Select(s => (Guid)s.Value)
            .Distinct()
            .ToList();

        if (suggestedCounterpartyIds.Count == 0) return results;

        var defaultsById = await _db.Counterparties
            .AsNoTracking()
            .Where(c => suggestedCounterpartyIds.Contains(c.Id))
            .Select(c => new CounterpartyDefaultsRow(
                c.Id, c.Name, c.DefaultCategoryId, c.DefaultMovementType, c.DefaultFinancialImpact))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        if (defaultsById.Count == 0) return results;

        return results
            .Select(r => r with { Suggestions = EnrichSuggestions(r.Suggestions, defaultsById) })
            .ToList();
    }

    private static IReadOnlyList<ClassificationSuggestion> EnrichSuggestions(
        IReadOnlyList<ClassificationSuggestion> suggestions,
        IReadOnlyDictionary<Guid, CounterpartyDefaultsRow> defaultsById)
    {
        var counterpartySuggestion = suggestions.FirstOrDefault(s => s.Dimension == SuggestionDimension.Counterparty);
        if (counterpartySuggestion is null) return suggestions;

        if (!defaultsById.TryGetValue((Guid)counterpartySuggestion.Value, out var defaults)) return suggestions;

        if (defaults.DefaultCategoryId is null
            && defaults.DefaultMovementType is null
            && defaults.DefaultFinancialImpact is null)
            return suggestions;

        var reason = $"Valor configurado por defecto para la contraparte \"{defaults.Name}\".";
        var merged = new List<ClassificationSuggestion>(suggestions);

        if (defaults.DefaultCategoryId is { } categoryId)
            MergeDimension(merged, SuggestionDimension.Category, categoryId, reason);

        if (defaults.DefaultMovementType is { } movementType)
            MergeDimension(merged, SuggestionDimension.MovementType, movementType, reason);

        if (defaults.DefaultFinancialImpact is { } financialImpact)
            MergeDimension(merged, SuggestionDimension.FinancialImpact, financialImpact, reason);

        return merged;
    }

    /// <summary>
    /// Mismo criterio de fusión ya definido para el frontend en PR-S5.1
    /// (<c>normalizeSuggestions</c> en <c>movements.html</c>): mayor
    /// <see cref="SuggestionConfidence"/> gana; en empate, se mantiene la primera —
    /// acá, la sugerencia histórica ya presente, porque la heurística 1 siempre corre
    /// (y se agrega a <c>suggestions</c>) antes que este enriquecimiento. Nunca dos
    /// sugerencias para la misma dimensión, la misma invariante que ya documenta
    /// <see cref="ClassificationSuggestionSet"/>.
    /// </summary>
    private static void MergeDimension(
        List<ClassificationSuggestion> suggestions, SuggestionDimension dimension, object value, string reason)
    {
        var candidate = new ClassificationSuggestion(dimension, value, SuggestionConfidence.High, reason);
        var existingIndex = suggestions.FindIndex(s => s.Dimension == dimension);

        if (existingIndex < 0)
        {
            suggestions.Add(candidate);
            return;
        }

        if (candidate.Confidence > suggestions[existingIndex].Confidence)
            suggestions[existingIndex] = candidate;
    }

    /// <summary>
    /// Une, mayúsculas e invariante de cultura, colapsando espacios internos repetidos.
    /// Deliberadamente ingenua (no fuzzy, no distancia de edición, no similitud): PR-S3
    /// es "exact match únicamente" — esto solo neutraliza diferencias triviales de
    /// espaciado/mayúsculas entre dos capturas del mismo texto bancario, no encuentra
    /// texto parecido.
    /// </summary>
    private static string Normalize(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var trimmed = description.Trim();
        var collapsed = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) collapsed.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                collapsed.Append(c);
                lastWasSpace = false;
            }
        }

        return collapsed.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Una recomendación por dimensión, a partir de las filas históricas con la misma
    /// descripción normalizada. Si todas coinciden en un valor, se sugiere con confianza
    /// Alta. Si el historial tiene más de un valor distinto para la misma descripción
    /// (el usuario clasificó distinto en distintas ocasiones), se sugiere el valor más
    /// reciente pero con confianza Media — no tiene sentido presentar como "alta
    /// confianza" algo sobre lo que el propio historial está en desacuerdo. Counterparty
    /// se trata aparte porque es la única dimensión opcional: filas sin contraparte no
    /// cuentan ni a favor ni en contra de una sugerencia de contraparte.
    /// </summary>
    private static IReadOnlyList<ClassificationSuggestion> BuildSuggestions(
        IReadOnlyList<ClassifiedHistoryRow> matches)
    {
        var suggestions = new List<ClassificationSuggestion>();
        var mostRecent = MostRecent(matches);

        AddDimensionSuggestion(
            suggestions, SuggestionDimension.Category,
            matches.Select(m => (object)m.CategoryId),
            mostRecent.CategoryId,
            matches.Count);

        AddDimensionSuggestion(
            suggestions, SuggestionDimension.MovementType,
            matches.Select(m => (object)m.MovementType),
            mostRecent.MovementType,
            matches.Count);

        AddDimensionSuggestion(
            suggestions, SuggestionDimension.FinancialImpact,
            matches.Select(m => (object)m.FinancialImpact),
            mostRecent.FinancialImpact,
            matches.Count);

        var withCounterparty = matches.Where(m => m.CounterpartyId is not null).ToList();
        if (withCounterparty.Count > 0)
        {
            var mostRecentWithCounterparty = MostRecent(withCounterparty);
            AddDimensionSuggestion(
                suggestions, SuggestionDimension.Counterparty,
                withCounterparty.Select(m => (object)m.CounterpartyId!.Value),
                mostRecentWithCounterparty.CounterpartyId!.Value,
                withCounterparty.Count);
        }

        return suggestions;
    }

    private static void AddDimensionSuggestion(
        List<ClassificationSuggestion> suggestions,
        SuggestionDimension dimension,
        IEnumerable<object> historicalValues,
        object mostRecentValue,
        int matchCount)
    {
        var distinctCount = historicalValues.Distinct().Count();
        var confidence = distinctCount == 1 ? SuggestionConfidence.High : SuggestionConfidence.Medium;
        var reason = distinctCount == 1
            ? $"{matchCount} clasificación{(matchCount == 1 ? "" : "es")} histórica{(matchCount == 1 ? "" : "s")} con la misma descripción, siempre con este valor."
            : $"{matchCount} clasificaciones históricas con la misma descripción, con {distinctCount} valores distintos — se propone el más reciente.";

        suggestions.Add(new ClassificationSuggestion(dimension, mostRecentValue, confidence, reason));
    }

    private static ClassifiedHistoryRow MostRecent(IReadOnlyList<ClassifiedHistoryRow> matches) =>
        matches.OrderByDescending(m => m.ProcessedAt).First();

    private static SourceEntityType ToSourceEntityType(MovementSource source) => source switch
    {
        MovementSource.BankDebit => SourceEntityType.BankStatement,
        MovementSource.CreditCard => SourceEntityType.Transaction,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "MovementSource sin mapeo a SourceEntityType conocido."),
    };

    private sealed record ClassifiedHistoryRow(
        string Description,
        Guid CategoryId,
        MovementType MovementType,
        FinancialImpact FinancialImpact,
        Guid? CounterpartyId,
        DateTime ProcessedAt);

    // PR-S7: proyección mínima de Counterparty para la heurística 2 — solo lo que
    // hace falta para enriquecer (Name para el Reason legible, los 3 Default*).
    private sealed record CounterpartyDefaultsRow(
        Guid Id,
        string Name,
        Guid? DefaultCategoryId,
        MovementType? DefaultMovementType,
        FinancialImpact? DefaultFinancialImpact);
}
