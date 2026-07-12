using System.Text;
using System.Text.RegularExpressions;
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
/// 1. (PR-S3, normalización mejorada en PR-S9, resolución de conflictos mejorada en
///    PR-S10, exclusión de categorías/contrapartes desactivadas en PR-S11) Exact match
///    de descripción normalizada contra el historial de <c>ClassifiedMovement</c> —
///    ver <see cref="BuildSuggestions"/>.
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
/// Por qué la heurística 1 no consulta <c>ClassifiedMovementItem</c> directamente,
/// aunque es una fuente de datos habilitada para este motor:
/// <c>ClassifiedMovement.Description</c> ya es la misma descripción original que
/// <c>ClassifiedMovementItem.OriginalDescription</c> (ambas se copian del mismo
/// <c>FinancialMovement.Description</c> al momento de clasificar, ver
/// <c>ClassifyMovementHandler</c>) — unirse a <c>Items</c> no aportaría una segunda
/// descripción distinta para esa regla. El contrato de <see cref="ClassificationSuggestion"/>
/// sugiere el <see cref="Guid"/> de la categoría/contraparte, no la entidad completa,
/// así que tampoco hace falta hidratar <c>Category</c>/<c>Counterparty</c> completas —
/// igual que <c>MovementView</c> ya expone <c>CategoryId</c>/<c>CounterpartyId</c> sin
/// hidratar la entidad. PR-S11 sí une (vía navegación, no una consulta aparte) contra
/// <c>Category</c>/<c>Counterparty</c> para leer únicamente su <c>IsDeactivated</c> —
/// ver <see cref="SuggestAsync"/> y <see cref="BuildSuggestions"/>. La heurística 2
/// también necesita consultar <c>Counterparty</c> (para leer sus valores por defecto),
/// en su propia consulta por lote — ver <see cref="EnrichWithCounterpartyDefaultsAsync"/>.
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
        // PR-S11: IsDeactivated de Category/Counterparty viaja en la misma consulta,
        // vía las navegaciones ya configuradas en ClassifiedMovementConfiguration
        // (join, no una consulta adicional) — se usa más abajo en BuildSuggestions para
        // que la heurística histórica nunca proponga una categoría o contraparte que el
        // usuario desactivó después de clasificar esos movimientos.
        var history = await _db.ClassifiedMovements
            .AsNoTracking()
            .Select(cm => new ClassifiedHistoryRow(
                cm.Description,
                cm.CategoryId,
                cm.MovementType,
                cm.FinancialImpact,
                cm.CounterpartyId,
                cm.ProcessedAt,
                cm.Category!.IsDeactivated,
                cm.Counterparty != null && cm.Counterparty.IsDeactivated))
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
    /// Monto en dólares embebido en la descripción (caso BBVA Visa, ver
    /// <c>BbvaTransactionLineParser</c> / <c>CurrencyDetector</c>): "USD 4,99",
    /// "USD 32.725,00". Mismo formato de monto argentino que ya usa
    /// <c>CurrencyDetector.UsdAmountRegex</c> — el monto factura distinto cada mes para
    /// el mismo comercio recurrente, así que no puede formar parte de la clave de
    /// comparación.
    /// </summary>
    private static readonly Regex EmbeddedUsdAmountPattern = new(
        @"\bUSD\s*(?:[\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Contador de cuotas embebido en la descripción (caso Mastercard, ver
    /// <c>MastercardTransactionLineParser.NormalizeDescription</c>, que ya deja este
    /// formato exacto "C1/3" persistido): distingue cuota individual, no comercio.
    /// </summary>
    private static readonly Regex InstallmentCounterPattern = new(
        @"\bC\d+/\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string StripEmbeddedUsdAmount(string description) =>
        EmbeddedUsdAmountPattern.Replace(description, string.Empty);

    private static string StripInstallmentCounter(string description) =>
        InstallmentCounterPattern.Replace(description, string.Empty);

    /// <summary>
    /// Une, mayúsculas e invariante de cultura, colapsando espacios internos repetidos.
    /// Deliberadamente ingenua (no fuzzy, no distancia de edición, no similitud): PR-S3
    /// es "exact match únicamente" — esto solo neutraliza diferencias triviales de
    /// espaciado/mayúsculas entre dos capturas del mismo texto bancario, no encuentra
    /// texto parecido.
    ///
    /// PR-S9 (ver docs/Architecture/PRS8analisisnormalizaciondescripciones.md) agrega
    /// dos reglas determinísticas más, aplicadas antes de colapsar espacios, respaldadas
    /// por datos reales de los parsers de tarjeta: <see cref="StripEmbeddedUsdAmount"/>
    /// quita el monto en dólares que BBVA Visa deja embebido para transacciones USD
    /// (ej. "PLAYSTATION USD 4,99" y "PLAYSTATION USD 9,99" pasan a ser el mismo
    /// comercio), y <see cref="StripInstallmentCounter"/> quita el contador de cuotas
    /// que Mastercard deja embebido (ej. "GARBARINO C1/3" y "GARBARINO C2/3" pasan a ser
    /// el mismo comercio). Ambas quitan únicamente un fragmento cuyo formato es
    /// inequívocamente un dato variable, nunca parte del nombre de un comercio — sin
    /// tocar dígitos ni símbolos genéricos (ver análisis de PR-S8, sección 3).
    ///
    /// Internal (no private) solo para exponerse a
    /// FinancialSystem.Infrastructure.Tests vía InternalsVisibleTo — sigue sin ser parte
    /// del contrato público, <see cref="IClassificationSuggestionService"/> no cambia.
    /// </summary>
    internal static string Normalize(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var withoutEmbeddedValues = StripInstallmentCounter(StripEmbeddedUsdAmount(description));

        var trimmed = withoutEmbeddedValues.Trim();
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
    /// descripción normalizada — ver <see cref="AddDimensionSuggestion"/> para cómo se
    /// elige el valor y la confianza. Counterparty se trata aparte porque es la única
    /// dimensión opcional: filas sin contraparte no cuentan ni a favor ni en contra de
    /// una sugerencia de contraparte.
    ///
    /// PR-S11: Category y Counterparty son también las dos únicas dimensiones que
    /// pueden apuntar a un catálogo que el usuario desactivó después de clasificar —
    /// filas con <see cref="ClassifiedHistoryRow.CategoryIsDeactivated"/>/
    /// <see cref="ClassifiedHistoryRow.CounterpartyIsDeactivated"/> se excluyen de la
    /// dimensión correspondiente antes de contar frecuencias, para que la heurística
    /// nunca proponga un valor que el catálogo activo (<c>GET /api/categories</c>/
    /// <c>/api/counterparties</c>, que ya filtran desactivados) no pueda mostrar. Esto
    /// respeta la independencia de las 4 dimensiones (ver doc-comment de
    /// <c>SuggestionDimension</c>): una categoría desactivada no descarta la fila
    /// completa, solo su voto en la dimensión Category — MovementType/FinancialImpact de
    /// esa misma fila siguen siendo evidencia válida. Si todas las filas de una
    /// dimensión quedan excluidas, esa dimensión simplemente no genera sugerencia
    /// (mismo patrón ya usado para "sin contraparte").
    /// </summary>
    internal static IReadOnlyList<ClassificationSuggestion> BuildSuggestions(
        IReadOnlyList<ClassifiedHistoryRow> matches)
    {
        // SuggestAsync nunca invoca este método con matches vacío (siempre viene de un
        // grupo de GroupBy, que por definición tiene al menos un elemento) — esta guarda
        // es defensiva, para que el método siga siendo seguro ahora que es internal y se
        // ejercita directamente desde los tests (ver PR-S10, caso "ausencia de historial").
        if (matches.Count == 0) return [];

        var suggestions = new List<ClassificationSuggestion>();

        var withActiveCategory = matches.Where(m => !m.CategoryIsDeactivated).ToList();
        if (withActiveCategory.Count > 0)
        {
            AddDimensionSuggestion(suggestions, SuggestionDimension.Category,
                withActiveCategory.Select(m => ((object)m.CategoryId, m.ProcessedAt)));
        }

        AddDimensionSuggestion(suggestions, SuggestionDimension.MovementType,
            matches.Select(m => ((object)m.MovementType, m.ProcessedAt)));

        AddDimensionSuggestion(suggestions, SuggestionDimension.FinancialImpact,
            matches.Select(m => ((object)m.FinancialImpact, m.ProcessedAt)));

        var withCounterparty = matches.Where(m => m.CounterpartyId is not null && !m.CounterpartyIsDeactivated).ToList();
        if (withCounterparty.Count > 0)
        {
            AddDimensionSuggestion(suggestions, SuggestionDimension.Counterparty,
                withCounterparty.Select(m => ((object)m.CounterpartyId!.Value, m.ProcessedAt)));
        }

        return suggestions;
    }

    /// <summary>
    /// PR-S10: el valor sugerido es el más frecuente en el historial (la moda), no el más
    /// reciente como hasta PR-S9 — con desacuerdo histórico, "qué contestó el usuario la
    /// mayoría de las veces" es más representativo que "qué contestó la última vez", y
    /// mantener el más reciente como valor propuesto habría dejado el <c>Reason</c> (que
    /// ahora describe la mayoría) potencialmente contradiciendo al propio valor sugerido.
    /// Empate entre los valores más frecuentes: se desempata por recencia (el más
    /// reciente entre los empatados), preservando el criterio de recencia anterior para
    /// el único caso donde la frecuencia sola no alcanza para decidir.
    ///
    /// Confianza: unánime (un solo valor en todo el historial) sigue siendo High, sin
    /// cambios. Cuando hay desacuerdo, en vez de colapsar siempre a Medium (como hasta
    /// PR-S9, sin distinguir "99 de 100 coinciden" de "51 de 100 coinciden"), se compara
    /// la proporción del valor ganador contra el umbral clásico de mayoría calificada
    /// (2/3) — la misma convención que ya se usa para mayorías calificadas en votaciones
    /// y gobernanza corporativa, no un porcentaje inventado para este caso puntual: al
    /// llegar a 2/3, el valor ganador ya tiene, como mínimo, el doble de clasificaciones
    /// que todos los demás valores combinados — ninguna redistribución posible de esos
    /// votos en un único rival alcanzaría para superarlo. Por debajo de esa proporción
    /// (incluye empates y "mayoría simple" apretada, ej. 51/49) la evidencia histórica es
    /// demasiado débil para justificar más que confianza Low — hoy nunca se producía Low
    /// porque esta distinción no existía.
    /// </summary>
    private static void AddDimensionSuggestion(
        List<ClassificationSuggestion> suggestions,
        SuggestionDimension dimension,
        IEnumerable<(object Value, DateTime ProcessedAt)> historicalEntries)
    {
        var groupedByValue = historicalEntries
            .GroupBy(e => e.Value)
            .Select(g => new { g.Key, Count = g.Count(), MostRecent = g.Max(e => e.ProcessedAt) })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.MostRecent)
            .ToList();

        var distinctCount = groupedByValue.Count;
        var winner = groupedByValue[0];
        var matchCount = groupedByValue.Sum(g => g.Count);

        var confidence = ResolveConfidence(distinctCount, winner.Count, matchCount);
        var reason = BuildReason(distinctCount, winner.Count, matchCount, confidence);

        suggestions.Add(new ClassificationSuggestion(dimension, winner.Key, confidence, reason));
    }

    /// <summary>
    /// Mayoría calificada (2/3): ver el doc-comment de <see cref="AddDimensionSuggestion"/>
    /// para la justificación de por qué este umbral no es un porcentaje arbitrario.
    /// </summary>
    private const double SupermajorityThreshold = 2.0 / 3.0;

    private static SuggestionConfidence ResolveConfidence(int distinctCount, int winnerCount, int matchCount)
    {
        if (distinctCount == 1) return SuggestionConfidence.High;

        return winnerCount >= matchCount * SupermajorityThreshold
            ? SuggestionConfidence.Medium
            : SuggestionConfidence.Low;
    }

    private static string BuildReason(int distinctCount, int winnerCount, int matchCount, SuggestionConfidence confidence)
    {
        if (distinctCount == 1)
            return $"{matchCount} clasificación{(matchCount == 1 ? "" : "es")} histórica{(matchCount == 1 ? "" : "s")} con la misma descripción, siempre con este valor.";

        if (confidence == SuggestionConfidence.Medium)
            return $"{matchCount} clasificaciones históricas con la misma descripción; mayoría amplia ({winnerCount} de {matchCount}) coincide en este valor.";

        return $"{matchCount} clasificaciones históricas con la misma descripción, con {distinctCount} valores distintos y sin mayoría clara ({winnerCount} de {matchCount} para el valor más frecuente).";
    }

    private static SourceEntityType ToSourceEntityType(MovementSource source) => source switch
    {
        MovementSource.BankDebit => SourceEntityType.BankStatement,
        MovementSource.CreditCard => SourceEntityType.Transaction,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "MovementSource sin mapeo a SourceEntityType conocido."),
    };

    // Internal (no private) solo para que FinancialSystem.Infrastructure.Tests pueda
    // construir filas y ejercitar BuildSuggestions directamente (ver PR-S10) — mismo
    // motivo que Normalize (PR-S9): sigue sin ser parte del contrato público.
    // PR-S11: CategoryIsDeactivated/CounterpartyIsDeactivated agregados al final para
    // no reordenar los campos existentes.
    internal sealed record ClassifiedHistoryRow(
        string Description,
        Guid CategoryId,
        MovementType MovementType,
        FinancialImpact FinancialImpact,
        Guid? CounterpartyId,
        DateTime ProcessedAt,
        bool CategoryIsDeactivated,
        bool CounterpartyIsDeactivated);

    // PR-S7: proyección mínima de Counterparty para la heurística 2 — solo lo que
    // hace falta para enriquecer (Name para el Reason legible, los 3 Default*).
    private sealed record CounterpartyDefaultsRow(
        Guid Id,
        string Name,
        Guid? DefaultCategoryId,
        MovementType? DefaultMovementType,
        FinancialImpact? DefaultFinancialImpact);
}
