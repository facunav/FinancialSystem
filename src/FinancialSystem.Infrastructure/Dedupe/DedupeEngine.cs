using System.Text.RegularExpressions;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Dedupe;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Dedupe;

/// <summary>
/// Implementación de <see cref="IDedupeEngine"/> — traducción directa a C# de la
/// especificación DEDUPE-003-CONV (señales A-M, matriz FUERTE/POSIBLE/INDETERMINADO/
/// DESCARTADO), validada mecánicamente en SQL sobre dataset sintético (DEDUPE-002.1-CONV/
/// 003-CONV) y con datos reales de `financialsystem` (reconciliación M, ver
/// DEDUPE-RECONCILIACION-IMPORT-vs-DEDUPE.md). No reimplementa ninguna regla nueva
/// que no haya sido demostrada en esa investigación.
///
/// Opera en memoria (carga los BankStatement relevantes con una sola query, igual que
/// SuspicionDetector con FinancialMovement) — el matching por regex/frecuencia no es
/// practicable como traducción LINQ-a-SQL, y el volumen de una cuenta real es chico.
/// </summary>
internal sealed class DedupeEngine : IDedupeEngine
{
    // Ventana de descubrimiento (DEDUPE-003-CONV sección 10): amplia y permisiva a
    // propósito, nunca es evidencia de identidad por sí misma — solo genera candidatos.
    private const int DiscoveryWindowDays = 10;

    // Guardián K (frecuencia de importe): demostrado mecánicamente por CASO G2 — un
    // importe con más de 1 aparición en la familia TRANSFERENCIA bloquea la vía
    // "único + cadena, sin número" hacia FUERTE.
    private const int FrequencyGuardThreshold = 1;

    private static readonly Regex NroPattern =
        new(@"\bNRO\.?:?\s*([0-9]{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpPattern =
        new(@"\bOP\.?:?\s*([0-9]{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public DedupeEngine(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DedupeCandidateResult>> PreviewAsync(
        IReadOnlyList<Guid>? focusBankStatementIds = null,
        CancellationToken cancellationToken = default)
    {
        var all = await _db.BankStatements
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var focusSet = focusBankStatementIds?.ToHashSet();
        var evaluated = Evaluate(all, focusSet);

        // DESCARTADO nunca se muestra como candidato -- ya cumplió su función (excluir
        // esa relación del conteo de competidores de la sección F).
        return evaluated.Where(r => r.Classification != IdentityClassification.Descartado).ToList();
    }

    public async Task<int> ApplyAsync(
        IReadOnlyList<DedupeCandidateResult> results,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var fuertes = results.Where(r => r.Classification == IdentityClassification.Fuerte).ToList();
        if (fuertes.Count == 0)
            return 0;

        // Idempotencia (ver PRE-FLIGHT sección G): consultar qué representaciones ya
        // tienen link antes de insertar nada -- nunca confiar solo en el índice único
        // para evitar una excepción de constraint en corridas repetidas.
        var candidateSourceIds = fuertes
            .SelectMany(r => new[] { r.PendienteId, r.LiquidadoId }.Concat(r.CarryForwardMemberIds))
            .Distinct()
            .ToList();

        var alreadyLinked = await _db.MovementIdentityLinks
            .Where(l => l.SourceEntityType == SourceEntityType.BankStatement
                        && candidateSourceIds.Contains(l.SourceId))
            .Select(l => l.SourceId)
            .ToHashSetAsync(cancellationToken);

        var now = _clock.UtcNow;
        var groupsCreated = 0;

        foreach (var result in fuertes)
        {
            var memberIds = new[] { result.PendienteId, result.LiquidadoId }
                .Concat(result.CarryForwardMemberIds)
                .Distinct()
                .ToList();

            // Si CUALQUIER miembro ya está linkeado, se saltea el grupo entero -- no se
            // genera un link parcial ni se toca el grupo existente.
            if (memberIds.Any(alreadyLinked.Contains))
                continue;

            var groupId = Guid.NewGuid();

            _db.MovementIdentityLinks.Add(new MovementIdentityLink
            {
                IdentityGroupId = groupId,
                SourceEntityType = SourceEntityType.BankStatement,
                SourceId = result.PendienteId,
                Role = IdentityRole.Pendiente,
                Classification = IdentityClassification.Fuerte,
                Evidence = result.Evidence,
                CreatedAtUtc = now,
                CreatedBy = createdBy,
            });

            _db.MovementIdentityLinks.Add(new MovementIdentityLink
            {
                IdentityGroupId = groupId,
                SourceEntityType = SourceEntityType.BankStatement,
                SourceId = result.LiquidadoId,
                Role = IdentityRole.Liquidado,
                Classification = IdentityClassification.Fuerte,
                Evidence = result.Evidence,
                CreatedAtUtc = now,
                CreatedBy = createdBy,
            });

            foreach (var carryForwardId in result.CarryForwardMemberIds)
            {
                _db.MovementIdentityLinks.Add(new MovementIdentityLink
                {
                    IdentityGroupId = groupId,
                    SourceEntityType = SourceEntityType.BankStatement,
                    SourceId = carryForwardId,
                    Role = IdentityRole.CarryForward,
                    Classification = IdentityClassification.Fuerte,
                    Evidence = result.Evidence,
                    CreatedAtUtc = now,
                    CreatedBy = createdBy,
                });
            }

            // Marcar como ya-linkeados dentro de esta misma corrida -- evita que dos
            // resultados de la MISMA llamada a ApplyAsync (ej. carry-forward mal armado)
            // generen dos grupos para el mismo miembro.
            foreach (var id in memberIds)
                alreadyLinked.Add(id);

            groupsCreated++;
        }

        if (groupsCreated > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return groupsCreated;
    }

    // ── Evaluación (traducción de la especificación DEDUPE-003-CONV a C#) ──────────

    internal IReadOnlyList<DedupeCandidateResult> Evaluate(
        IReadOnlyList<BankStatement> all,
        IReadOnlySet<Guid>? focusIds)
    {
        var rows = all.Select(BuildRow).ToList();

        var chainOk = ComputeLocalChainOk(all);
        foreach (var row in rows)
            row.ChainOk = chainOk.TryGetValue(row.Statement.Id, out var ok) && ok;

        // Señal K: frecuencia de importe entre TRANSFERENCIA/TRANSFERENCIA INMEDIATA --
        // contada por IDENTIDAD ECONÓMICA, no por fila física (fix DEDUPE-004-CONV,
        // auditoría de K sobre datos reales de financialsystem: la cuenta reexporta el
        // mismo movimiento en extractos acumulativos sucesivos -- misma Fecha+Concepto+
        // Importe+Balance+Balance siguiente en más de un SourceFile -- y esas copias
        // físicas de una sola reexportación no son competidores reales). Dos filas
        // colapsan a una sola identidad únicamente cuando ese fingerprint completo
        // coincide; si Balance o el saldo de la fila siguiente no están disponibles para
        // alguna fila, esa fila nunca colapsa con ninguna otra -- se sigue contando por
        // separado, para no bajar la guardia sin evidencia (verificado contra los
        // controles negativos reales: -50000/-19000/-5000/13000 no colapsan porque, en al
        // menos un caso, comparten fecha+importe pero tienen Balance distinto).
        var nextBalance = ComputeNextBalance(all);
        var frequencyByAmount = rows
            .Where(r => r.ConceptNormalized is "TRANSFERENCIA" or "TRANSFERENCIA INMEDIATA")
            .Select(r =>
            {
                var saldo = r.Statement.Balance;
                var saldoSiguiente = nextBalance.GetValueOrDefault(r.Statement.Id);
                var fingerprintCompleto = saldo is not null && saldoSiguiente is not null;
                return new
                {
                    r.Statement.Amount,
                    IdentityKey = (
                        r.Statement.Date.Date,
                        r.ConceptNormalized,
                        r.Statement.Amount,
                        Saldo: fingerprintCompleto ? saldo : null,
                        SaldoSiguiente: fingerprintCompleto ? saldoSiguiente : null,
                        // Sin fingerprint completo, cada fila es su propia identidad --
                        // nunca colapsa con otra por suposición.
                        Disambiguator: fingerprintCompleto ? (Guid?)null : r.Statement.Id)
                };
            })
            .GroupBy(x => x.IdentityKey)
            .Select(g => g.Key.Amount)
            .GroupBy(amount => amount)
            .ToDictionary(g => g.Key, g => g.Count());

        // Señal M: Nro completo asociado a más de un importe distinto en toda la cuenta.
        var genericNros = rows
            .Where(r => r.Nro is not null)
            .GroupBy(r => r.Nro!)
            .Where(g => g.Select(r => r.Statement.Amount).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        // Ancla por importe (fix E.1): evita emparejar entre sí dos filas sin ancla
        // cuando SÍ existe un ancla real en otra fila con el mismo importe.
        var amountsWithAnchor = rows
            .Where(r => r.EsFormaNro)
            .Select(r => r.Statement.Amount)
            .ToHashSet();

        var results = new List<DedupeCandidateResult>();

        foreach (var pendiente in rows)
        {
            if (focusIds is not null && !focusIds.Contains(pendiente.Statement.Id))
                continue;

            var rawPairs = new List<Row>();
            foreach (var liquidado in rows)
            {
                if (!IsCandidatePair(pendiente, liquidado, amountsWithAnchor))
                    continue;
                rawPairs.Add(liquidado);
            }

            if (rawPairs.Count == 0)
                continue;

            // Colapso de carry-forward: candidatos con firma idéntica (fecha + concepto
            // normalizado) cuentan como una sola "bucket" -- DEDUPE-003-CONV sección I.
            var buckets = rawPairs
                .GroupBy(l => (l.Statement.Date.Date, l.ConceptNormalized))
                .Select(g => new Bucket(g.Key.Date, g.Key.ConceptNormalized, g.ToList()))
                .ToList();

            // Conteo de competidores tipo A, EXCLUYENDO relaciones tipo B (contradicción
            // de identificador) del conteo -- hallazgo E.6, formalizado en
            // DEDUPE-003-CONV sección G, implementado acá por primera vez (nunca se
            // corrigió en el clasificador SQL de validación).
            var realCompetitorBuckets = buckets.Count(b => !NumeroContradice(pendiente, b));

            foreach (var bucket in buckets)
            {
                var representative = bucket.Members[0];
                var contradice = NumeroContradice(pendiente, bucket);
                var coincide = NumeroCoincide(pendiente, bucket);

                IdentityClassification classification;
                string evidence;

                if (contradice)
                {
                    classification = IdentityClassification.Descartado;
                    evidence = $"D: identificador contradictorio (Nro pendiente={pendiente.Numero}, " +
                               $"numero liquidado={representative.Numero})";
                }
                else if (realCompetitorBuckets > 1)
                {
                    classification = IdentityClassification.Indeterminado;
                    evidence = $"L: {realCompetitorBuckets} candidatos igualmente plausibles tras colapsar carry-forward";
                }
                else if (coincide && !(pendiente.Nro is not null && genericNros.Contains(pendiente.Nro)))
                {
                    classification = IdentityClassification.Fuerte;
                    evidence = $"D+E: transformación Nro->OP demostrada (sufijo {Right4(pendiente.Numero)})";
                }
                else if (coincide)
                {
                    // M bloqueó lo que D+E habría dado por FUERTE.
                    classification = IdentityClassification.Posible;
                    evidence = $"M: Nro {pendiente.Nro} asociado a más de un importe en la cuenta -- " +
                               "guardián bloquea la vía D+E pese a coincidencia de sufijo";
                }
                else
                {
                    var liquidadoSinNumero = representative.Numero is null;
                    var freq = frequencyByAmount.GetValueOrDefault(pendiente.Statement.Amount, 0);
                    var anyChainOk = bucket.Members.Any(m => m.ChainOk);

                    if (liquidadoSinNumero && freq <= FrequencyGuardThreshold && pendiente.ChainOk && anyChainOk)
                    {
                        classification = IdentityClassification.Fuerte;
                        evidence = "F+K+L: único candidato + cadena de Balance exacta en ambos lados, " +
                                   $"sin número, frecuencia={freq}";
                    }
                    else if (liquidadoSinNumero)
                    {
                        classification = IdentityClassification.Posible;
                        var reasons = new List<string>();
                        if (freq > FrequencyGuardThreshold)
                            reasons.Add($"K: frecuencia de importe={freq} (>{FrequencyGuardThreshold}) bloquea la vía única+cadena");
                        if (!pendiente.ChainOk || !anyChainOk)
                            reasons.Add($"F: cadena de Balance no confirma en ambos lados (pendiente={pendiente.ChainOk}, liquidado={anyChainOk})");
                        evidence = reasons.Count > 0
                            ? "Sin número en el liquidado -- " + string.Join("; ", reasons)
                            : "Sin número en el liquidado -- transformación compatible pero prueba insuficiente";
                    }
                    else
                    {
                        classification = IdentityClassification.Posible;
                        evidence = "Sin transformación validada";
                    }
                }

                var carryForwardIds = bucket.Members.Skip(1).Select(m => m.Statement.Id).ToList();

                results.Add(new DedupeCandidateResult(
                    pendiente.Statement.Id,
                    pendiente.Statement.Concept,
                    pendiente.Statement.Date,
                    pendiente.Statement.Amount,
                    pendiente.Statement.SourceFile,
                    representative.Statement.Id,
                    representative.Statement.Concept,
                    representative.Statement.Date,
                    representative.Statement.Amount,
                    representative.Statement.SourceFile,
                    classification,
                    evidence,
                    carryForwardIds));
            }
        }

        // ── Duplicado exacto (DEDUPE-003-CONV sección B paso 1 / apéndice caso #16) ──
        // "Fecha+Importe+Concepto idénticos entre archivos -> FUERTE trivial, no pasa
        // por el resto del pipeline." IsCandidatePair excluye a propósito este par del
        // pipeline normal (ver el comentario ahí) -- pero esa exclusión no tenía
        // contraparte: el par desaparecía sin resultado en vez de resolverse por la vía
        // trivial que exige la especificación (bug real, DEDUPE-004-CONV). Esta vía es
        // ADITIVA y posterior al pipeline normal de arriba: nunca compite con un
        // resultado ya emitido para las mismas filas (yaCubiertos) -- los casos que
        // además tienen un lado con concepto distinto (326888/684228, ya resueltos por
        // F+K+L más arriba) no generan un segundo resultado.
        var yaCubiertos = results
            .SelectMany(r => new[] { r.PendienteId, r.LiquidadoId }.Concat(r.CarryForwardMemberIds))
            .ToHashSet();

        var clustersExactos = rows
            .GroupBy(r => (r.Statement.Date.Date, r.ConceptNormalized, r.Statement.Amount))
            .Where(g => g.Select(r => r.Statement.SourceFile).Distinct().Count() > 1)
            .SelectMany(g => AgruparPorFingerprintDeBalance(g, nextBalance));

        foreach (var miembros in clustersExactos)
        {
            if (miembros.Count < 2) continue;
            if (miembros.Select(m => m.Statement.SourceFile).Distinct().Count() < 2) continue;
            if (miembros.Any(m => yaCubiertos.Contains(m.Statement.Id))) continue;
            if (focusIds is not null && !miembros.Any(m => focusIds.Contains(m.Statement.Id))) continue;

            // Orden canónico por Id -- mismo criterio de desempate ya usado en el
            // roleOk de IsCandidatePair -- para no emitir A→B y B→A como dos
            // resultados de la misma identidad (regla 10).
            var ordenados = miembros.OrderBy(m => m.Statement.Id).ToList();
            var primero = ordenados[0];
            var segundo = ordenados[1];
            var extras = ordenados.Skip(2).Select(m => m.Statement.Id).ToList();

            results.Add(new DedupeCandidateResult(
                primero.Statement.Id,
                primero.Statement.Concept,
                primero.Statement.Date,
                primero.Statement.Amount,
                primero.Statement.SourceFile,
                segundo.Statement.Id,
                segundo.Statement.Concept,
                segundo.Statement.Date,
                segundo.Statement.Amount,
                segundo.Statement.SourceFile,
                IdentityClassification.Fuerte,
                $"B: duplicado exacto -- Fecha+Importe+Concepto+Balance idénticos en " +
                $"{ordenados.Count} archivos, no pasa por el resto del pipeline " +
                "(DEDUPE-003-CONV, apéndice caso #16)",
                extras));
        }

        return results;
    }

    // Sub-agrupa un cluster de "misma Fecha+Concepto+Importe" por el mismo fingerprint
    // de Balance que ya usa la señal K (saldo propio + saldo de la fila siguiente en su
    // archivo, vía ComputeNextBalance) -- un duplicado exacto real exige, además, el
    // mismo saldo antes/después; sin ese dato disponible en ambos lados, nunca se asume
    // la identidad (mismo criterio conservador que el fix de K en frequencyByAmount).
    private static IEnumerable<List<Row>> AgruparPorFingerprintDeBalance(
        IEnumerable<Row> candidatos, IReadOnlyDictionary<Guid, decimal?> nextBalance)
    {
        return candidatos
            .Select(r => new
            {
                Row = r,
                Saldo = r.Statement.Balance,
                SaldoSiguiente = nextBalance.GetValueOrDefault(r.Statement.Id)
            })
            .Where(x => x.Saldo is not null && x.SaldoSiguiente is not null)
            .GroupBy(x => (x.Saldo, x.SaldoSiguiente))
            .Select(g => g.Select(x => x.Row).ToList());
    }

    private static bool IsCandidatePair(Row a, Row b, HashSet<decimal> amountsWithAnchor)
    {
        if (a.Statement.Id == b.Statement.Id) return false;
        if (a.Statement.Amount != b.Statement.Amount) return false;
        if (a.Statement.SourceFile is null || b.Statement.SourceFile is null) return false;
        if (a.Statement.SourceFile == b.Statement.SourceFile) return false;
        if (Math.Abs((a.Statement.Date.Date - b.Statement.Date.Date).Days) > DiscoveryWindowDays) return false;

        // Ya resuelto como duplicado exacto (misma fecha+concepto normalizado) -- lo
        // cubre la idempotencia por ExternalId / la vía de duplicado exacto de
        // BbvaBankStatementImporter, no entra al resto del pipeline.
        if (a.ConceptNormalized == b.ConceptNormalized && a.Statement.Date.Date == b.Statement.Date.Date)
            return false;

        // Asignación de rol (fix del bug de orden arbitrario por Id encontrado en
        // DEDUPE-002.1-CONV): si un solo lado tiene forma Nro, ese lado es "pendiente".
        var roleOk = (a.EsFormaNro && !b.EsFormaNro)
                     || (a.EsFormaNro == b.EsFormaNro && a.Statement.Id.CompareTo(b.Statement.Id) < 0);
        if (!roleOk) return false;

        // Fix E.1: par espurio si ninguno de los dos tiene ancla PERO existe un ancla
        // real en otra fila con el mismo importe -- son candidatos rivales del mismo
        // pendiente, no un par independiente.
        if (!a.EsFormaNro && !b.EsFormaNro && amountsWithAnchor.Contains(a.Statement.Amount))
            return false;

        return true;
    }

    private static bool NumeroCoincide(Row pendiente, Bucket bucket) =>
        pendiente.Numero is not null
        && bucket.Members[0].Numero is not null
        && Right4(pendiente.Numero) == Right4(bucket.Members[0].Numero);

    private static bool NumeroContradice(Row pendiente, Bucket bucket) =>
        pendiente.Numero is not null
        && bucket.Members[0].Numero is not null
        && Right4(pendiente.Numero) != Right4(bucket.Members[0].Numero);

    private static string? Right4(string? numero) =>
        numero is null ? null : numero.Length <= 4 ? numero : numero[^4..];

    private static Row BuildRow(BankStatement statement)
    {
        var concept = statement.Concept ?? string.Empty;
        var nroMatch = NroPattern.Match(concept);
        var opMatch = OpPattern.Match(concept);
        var nro = nroMatch.Success ? nroMatch.Groups[1].Value : null;
        var numero = nro ?? (opMatch.Success ? opMatch.Groups[1].Value : null);

        return new Row(statement, NormalizeConcept(concept), nro, numero, nro is not null);
    }

    private static string NormalizeConcept(string concept) =>
        Regex.Replace(concept, @"\s+", " ").Trim().ToUpperInvariant();

    private static Dictionary<Guid, bool> ComputeLocalChainOk(IReadOnlyList<BankStatement> all)
    {
        var result = new Dictionary<Guid, bool>();

        foreach (var group in all.Where(s => s.SourceFile is not null && s.RowNumber.HasValue)
                                  .GroupBy(s => s.SourceFile))
        {
            var ordered = group.OrderBy(s => s.RowNumber).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var current = ordered[i];
                var next = ordered[i + 1];
                if (current.Balance is not decimal saldo || next.Balance is not decimal saldoSiguiente)
                    continue;

                result[current.Id] = saldo - current.Amount == saldoSiguiente;
            }
        }

        return result;
    }

    // Balance de la fila siguiente (mismo SourceFile, RowNumber inmediato superior) --
    // usado exclusivamente por el colapso de identidad económica de la señal K (ver
    // frequencyByAmount en Evaluate). Misma noción de "fila siguiente en el archivo" que
    // ComputeLocalChainOk (señal F), calculada por separado para no modificar F.
    private static Dictionary<Guid, decimal?> ComputeNextBalance(IReadOnlyList<BankStatement> all)
    {
        var result = new Dictionary<Guid, decimal?>();

        foreach (var group in all.Where(s => s.SourceFile is not null && s.RowNumber.HasValue)
                                  .GroupBy(s => s.SourceFile))
        {
            var ordered = group.OrderBy(s => s.RowNumber).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
                result[ordered[i].Id] = ordered[i + 1].Balance;
        }

        return result;
    }

    private sealed class Row
    {
        public Row(BankStatement statement, string conceptNormalized, string? nro, string? numero, bool esFormaNro)
        {
            Statement = statement;
            ConceptNormalized = conceptNormalized;
            Nro = nro;
            Numero = numero;
            EsFormaNro = esFormaNro;
        }

        public BankStatement Statement { get; }
        public string ConceptNormalized { get; }
        public string? Nro { get; }
        public string? Numero { get; }
        public bool EsFormaNro { get; }
        public bool ChainOk { get; set; }
    }

    private sealed record Bucket(DateTime Date, string ConceptNormalized, List<Row> Members);
}
