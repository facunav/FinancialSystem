using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Review;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Review;

/// <summary>
/// Cobertura de <see cref="SuspicionDetector"/> — documenta el comportamiento actual
/// del detector (duplicados y splits), no un comportamiento deseado. Ningún test acá
/// corrige ni asume un cambio de algoritmo; donde el comportamiento real es
/// sorprendente pero consistente (encadenamiento transitivo de duplicados,
/// insensibilidad al signo, "primer match gana" en splits ambiguos, el límite de
/// <c>MaxSplitPartsConsidered</c>), se documenta explícitamente con un comentario y
/// se deja tal cual — ver PATCH-036.
///
/// <see cref="SuspicionDetector"/> es internal; este proyecto de tests lo referencia
/// vía <c>InternalsVisibleTo</c> (mismo mecanismo que ya usan los tests de
/// <c>ClassificationSuggestionService</c>), sin exponer nada nuevo del contrato público.
/// </summary>
public class SuspicionDetectorTests
{
    // Fecha base fija (sin componente de hora) para que las comparaciones de ventana
    // (que truncan a Date) sean exactas y no dependan de la hora de ejecución del test.
    private static readonly DateTime BaseDate = new(2026, 1, 15);

    private static SuspicionDetector CreateDetector(ReviewEngineOptions? options = null) =>
        new(Options.Create(options ?? new ReviewEngineOptions()));

    private static FinancialMovement Movement(decimal amount, DateTime? date = null, string description = "Movimiento") =>
        new()
        {
            SourceId = Guid.NewGuid(),
            Date = date ?? BaseDate,
            Description = description,
            Amount = amount,
            Source = MovementSource.BankDebit,
        };

    // ── Duplicados ───────────────────────────────────────────────────────────

    [Fact]
    public void Detect_DosMovimientosMismoMontoYFecha_FormaGrupoDeDuplicados()
    {
        var a = Movement(100m);
        var b = Movement(100m);

        var groups = CreateDetector().Detect([a, b]);

        var group = Assert.Single(groups);
        Assert.Equal(SuspicionReason.PossibleDuplicate, group.Reason);
        Assert.Equal(2, group.Movements.Count);
        Assert.Contains(a, group.Movements);
        Assert.Contains(b, group.Movements);
    }

    [Fact]
    public void Detect_TresMovimientosEncadenadosPorTolerancia_FormanUnSoloGrupoAunqueLosExtremosNoSeanDuplicadosEntreSi()
    {
        // A-B difieren en 1 (dentro de tolerancia default=1), B-C difieren en 1 también,
        // pero A-C difieren en 2 (fuera de tolerancia). El detector agrupa por
        // componente conexa del grafo de pares "posible duplicado", no por clique --
        // A y C terminan en el mismo grupo por transitividad a través de B, aunque
        // A y C solos nunca se habrían marcado como duplicados entre sí. Comportamiento
        // actual documentado tal cual, no una corrección.
        var a = Movement(100m);
        var b = Movement(101m);
        var c = Movement(102m);

        var groups = CreateDetector().Detect([a, b, c]);

        var group = Assert.Single(groups);
        Assert.Equal(SuspicionReason.PossibleDuplicate, group.Reason);
        Assert.Equal(3, group.Movements.Count);
        Assert.Contains(a, group.Movements);
        Assert.Contains(b, group.Movements);
        Assert.Contains(c, group.Movements);
    }

    [Fact]
    public void Detect_CuatroMovimientosIdenticos_FormaUnUnicoGrupoDeCuatroNoDosPares()
    {
        var movements = Enumerable.Range(0, 4).Select(_ => Movement(50m)).ToList();

        var groups = CreateDetector().Detect(movements);

        var group = Assert.Single(groups);
        Assert.Equal(4, group.Movements.Count);
    }

    [Fact]
    public void Detect_DosGruposDeDuplicadosIndependientes_DetectaAmbosPorSeparado()
    {
        var a1 = Movement(100m);
        var a2 = Movement(100m);
        var b1 = Movement(5000m);
        var b2 = Movement(5000m);

        var groups = CreateDetector().Detect([a1, a2, b1, b2]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(SuspicionReason.PossibleDuplicate, g.Reason));
        Assert.Contains(groups, g => g.Movements.Contains(a1) && g.Movements.Contains(a2) && g.Movements.Count == 2);
        Assert.Contains(groups, g => g.Movements.Contains(b1) && g.Movements.Contains(b2) && g.Movements.Count == 2);
    }

    [Fact]
    public void Detect_MovimientoSinNadaParecidoCerca_NoFormaGrupo()
    {
        var a = Movement(100m);
        var unrelated = Movement(9999m);

        var groups = CreateDetector().Detect([a, unrelated]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_DiferenciaDeMontoIgualATolerancia_EsDuplicado()
    {
        // Tolerancia default = 1m. Diferencia exacta = 1m -- el chequeo es "<=", así
        // que el límite mismo todavía cuenta como duplicado.
        var a = Movement(100m);
        var b = Movement(101m);

        var groups = CreateDetector().Detect([a, b]);

        Assert.Single(groups);
    }

    [Fact]
    public void Detect_DiferenciaDeMontoApenasSuperaLaTolerancia_NoEsDuplicado()
    {
        var a = Movement(100m);
        var b = Movement(101.01m);

        var groups = CreateDetector().Detect([a, b]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_DiferenciaDeMontoMinima_SigueSiendoDuplicado()
    {
        var a = Movement(100m);
        var b = Movement(100.01m);

        var groups = CreateDetector().Detect([a, b]);

        Assert.Single(groups);
    }

    [Fact]
    public void Detect_FechasDistintasDentroDeLaVentana_SonCandidatasADuplicado()
    {
        // Ventana default = 1 día.
        var a = Movement(100m, BaseDate);
        var b = Movement(100m, BaseDate.AddDays(1));

        var groups = CreateDetector().Detect([a, b]);

        Assert.Single(groups);
    }

    [Fact]
    public void Detect_FechasFueraDeLaVentana_NoSonCandidatasADuplicado()
    {
        var a = Movement(100m, BaseDate);
        var b = Movement(100m, BaseDate.AddDays(2));

        var groups = CreateDetector().Detect([a, b]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_OrdenDeEntradaDistinto_ProduceElMismoGrupoComoConjunto()
    {
        var a = Movement(100m);
        var b = Movement(101m);
        var c = Movement(102m);

        var inOriginalOrder = CreateDetector().Detect([a, b, c]);
        var inReversedOrder = CreateDetector().Detect([c, b, a]);

        HashSet<Guid> IdsOfTheOnlyGroup(IReadOnlyList<SuspiciousGroup> groups) =>
            Assert.Single(groups).Movements.Select(m => m.Id).ToHashSet();

        // SetEquals en vez de Assert.Equal sobre los HashSet: importa el conjunto de
        // Ids, no el orden de enumeración (que HashSet no garantiza estable entre
        // instancias distintas aunque el contenido sea el mismo).
        Assert.True(IdsOfTheOnlyGroup(inOriginalOrder).SetEquals(IdsOfTheOnlyGroup(inReversedOrder)));
    }

    [Fact]
    public void Detect_MontoPositivoYNegativoConMismoValorAbsoluto_SeConsideranDuplicados()
    {
        // IsPossibleDuplicate compara Math.Abs(a.Amount) contra Math.Abs(b.Amount) --
        // un gasto y un ingreso del mismo valor absoluto quedan marcados como posible
        // duplicado entre sí. Comportamiento actual, documentado tal cual.
        var expense = Movement(100m);
        var income = Movement(-100m);

        var groups = CreateDetector().Detect([expense, income]);

        var group = Assert.Single(groups);
        Assert.Equal(SuspicionReason.PossibleDuplicate, group.Reason);
    }

    // ── Splits ───────────────────────────────────────────────────────────────

    [Fact]
    public void Detect_DosMovimientosSumanElTotalDeOtro_FormaGrupoDeSplit()
    {
        var total = Movement(1000m);
        var part1 = Movement(600m);
        var part2 = Movement(400m);

        var groups = CreateDetector().Detect([total, part1, part2]);

        var group = Assert.Single(groups);
        Assert.Equal(SuspicionReason.SplitTransaction, group.Reason);
        Assert.Equal(3, group.Movements.Count);
        Assert.Contains(total, group.Movements);
        Assert.Contains(part1, group.Movements);
        Assert.Contains(part2, group.Movements);
    }

    [Fact]
    public void Detect_TresMovimientosSumanElTotalDeOtro_FormaGrupoDeSplitDeTres()
    {
        // Montos distintos entre sí (para no disparar además un grupo de duplicados
        // entre las partes) y ninguna pareja suma cerca de 900 -- el detector solo
        // encuentra el split al llegar a la combinación de a 3.
        var total = Movement(900m);
        var part1 = Movement(250m);
        var part2 = Movement(300m);
        var part3 = Movement(350m);

        var groups = CreateDetector().Detect([total, part1, part2, part3]);

        var group = Assert.Single(groups);
        Assert.Equal(SuspicionReason.SplitTransaction, group.Reason);
        Assert.Equal(4, group.Movements.Count);
    }

    [Fact]
    public void Detect_SumaDeLasPartesIgualALaToleranciaDelTotal_EsSplit()
    {
        var total = Movement(1000m);
        var part1 = Movement(600m);
        var part2 = Movement(399m); // suma 999, diferencia = 1 = tolerancia

        var groups = CreateDetector().Detect([total, part1, part2]);

        Assert.Single(groups);
    }

    [Fact]
    public void Detect_SumaDeLasPartesApenasSuperaLaTolerancia_NoEsSplit()
    {
        var total = Movement(1000m);
        var part1 = Movement(600m);
        var part2 = Movement(398.99m); // suma 998.99, diferencia = 1.01

        var groups = CreateDetector().Detect([total, part1, part2]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_NingunaCombinacionSumaElTotal_NoDetectaSplit()
    {
        var total = Movement(1000m);
        var a = Movement(1m);
        var b = Movement(50m);
        var c = Movement(9999m);

        var groups = CreateDetector().Detect([total, a, b, c]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_SplitQueRequiereCuatroPartes_NoSeDetecta()
    {
        // El algoritmo solo evalúa combinaciones de 2 o 3 partes (FindSplit) -- un
        // split real de 4 movimientos, donde ninguna pareja ni tripleta suma cerca
        // del total, queda sin detectar. Comportamiento actual, documentado tal cual,
        // no una corrección.
        var total = Movement(400m);
        var parts = new[] { Movement(50m), Movement(90m), Movement(120m), Movement(140m) }; // suman 400 entre las 4

        var groups = CreateDetector().Detect([total, .. parts]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_CombinacionAmbigua_EligeElPrimerParEnOrdenDeAparicionEnLaLista()
    {
        // Tanto (a,b) como (c,d) suman el total -- el algoritmo recorre "parts" en el
        // orden en que aparecen en la lista de entrada (menos el total) y devuelve la
        // primera combinación que encuentra, sin evaluar cuál es "mejor". Con este
        // orden de entrada, (a,b) aparece antes que (c,d) en la iteración y gana.
        var total = Movement(1000m);
        var a = Movement(300m);
        var b = Movement(700m);
        var c = Movement(400m);
        var d = Movement(600m);

        var groups = CreateDetector().Detect([total, a, b, c, d]);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Movements.Count);
        Assert.Contains(a, group.Movements);
        Assert.Contains(b, group.Movements);
        Assert.DoesNotContain(c, group.Movements);
        Assert.DoesNotContain(d, group.Movements);
    }

    [Fact]
    public void Detect_CombinacionAmbigua_ReordenarLaEntradaCambiaElParElegido()
    {
        // Mismos movimientos que el test anterior, pero (c,d) aparecen antes que (a,b)
        // en la lista de entrada -- ahora (c,d) es la primera combinación encontrada.
        // Confirma que la elección depende del orden de entrada, no de ningún criterio
        // de "mejor combinación".
        var total = Movement(1000m);
        var a = Movement(300m);
        var b = Movement(700m);
        var c = Movement(400m);
        var d = Movement(600m);

        var groups = CreateDetector().Detect([total, c, d, a, b]);

        var group = Assert.Single(groups);
        Assert.Contains(c, group.Movements);
        Assert.Contains(d, group.Movements);
        Assert.DoesNotContain(a, group.Movements);
        Assert.DoesNotContain(b, group.Movements);
    }

    [Fact]
    public void Detect_PartesConSignoOpuestoAlTotalQueSumanElValorAbsoluto_SeConsideraSplit()
    {
        // FindSplit compara Math.Abs(total.Amount) contra la suma de Math.Abs de cada
        // parte -- un total "ingreso" (negativo) puede quedar marcado como split de
        // partes "gasto" (positivas) cuyo valor absoluto suma igual. Comportamiento
        // actual documentado tal cual.
        var total = Movement(-500m);
        var part1 = Movement(300m);
        var part2 = Movement(200m);

        var groups = CreateDetector().Detect([total, part1, part2]);

        var group = Assert.Single(groups);
        Assert.Equal(SuspicionReason.SplitTransaction, group.Reason);
    }

    [Fact]
    public void Detect_UnMovimientoPuedeSerParteDeMasDeUnGrupoDeSplit()
    {
        // alreadyMatchedAsTotal solo evita que el mismo movimiento vuelva a
        // procesarse como "total" -- no excluye a un movimiento ya usado como
        // "parte" de participar en otro split distinto. T1 se resuelve con
        // (part1, part2); T2 se resuelve con (part1, y) -- part1 termina en
        // ambos grupos.
        var t1 = Movement(1000m, description: "T1");
        var t2 = Movement(700m, description: "T2");
        var part1 = Movement(600m, description: "part1");
        var part2 = Movement(400m, description: "part2");
        var y = Movement(100m, description: "y");

        var groups = CreateDetector().Detect([t1, t2, part1, part2, y]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(SuspicionReason.SplitTransaction, g.Reason));

        var t1Group = Assert.Single(groups, g => g.Movements.Contains(t1));
        Assert.Contains(part1, t1Group.Movements);
        Assert.Contains(part2, t1Group.Movements);

        var t2Group = Assert.Single(groups, g => g.Movements.Contains(t2));
        Assert.Contains(part1, t2Group.Movements);
        Assert.Contains(y, t2Group.Movements);
    }

    [Fact]
    public void Detect_PartesFueraDeLaVentanaTemporalDelTotal_NoSeConsideranCandidatas()
    {
        var total = Movement(1000m, BaseDate);
        var part1 = Movement(600m, BaseDate.AddDays(2)); // fuera de la ventana default (1 día)
        var part2 = Movement(400m, BaseDate.AddDays(2));

        var groups = CreateDetector().Detect([total, part1, part2]);

        Assert.Empty(groups);
    }

    // ── Casos límite ─────────────────────────────────────────────────────────

    [Fact]
    public void Detect_ColeccionVacia_NoDetectaNada()
    {
        var groups = CreateDetector().Detect([]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_UnSoloMovimiento_NoDetectaNada()
    {
        var groups = CreateDetector().Detect([Movement(100m)]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_SuspicionReasonRoundingAnomaly_NuncaSeProduce()
    {
        // SuspicionReason declara un tercer valor, RoundingAnomaly, que ningún método
        // de SuspicionDetector asigna hoy -- solo PossibleDuplicate y SplitTransaction
        // se producen realmente. Se documenta con un escenario que dispara ambos casos
        // reales para confirmar que ninguno de los dos resulta en RoundingAnomaly.
        var dupA = Movement(100m);
        var dupB = Movement(100m);
        var total = Movement(1000m);
        var part1 = Movement(600m);
        var part2 = Movement(400m);

        var groups = CreateDetector().Detect([dupA, dupB, total, part1, part2]);

        Assert.Equal(2, groups.Count);
        Assert.DoesNotContain(groups, g => g.Reason == SuspicionReason.RoundingAnomaly);
    }

    // ── Parámetros internos ──────────────────────────────────────────────────

    [Fact]
    public void Detect_ToleranciaDeMontoConfigurada_SeRespetaEnDuplicados()
    {
        // Diferencia de 5 -- fuera de la tolerancia default (1), dentro de una
        // tolerancia configurada explícitamente en 5.
        var a = Movement(100m);
        var b = Movement(105m);
        var options = new ReviewEngineOptions { DuplicateAmountTolerance = 5m };

        var withWiderTolerance = CreateDetector(options).Detect([a, b]);
        var withDefaultTolerance = CreateDetector().Detect([a, b]);

        Assert.Single(withWiderTolerance);
        Assert.Empty(withDefaultTolerance);
    }

    [Fact]
    public void Detect_VentanaTemporalConfigurada_SeRespetaEnDuplicados()
    {
        // 3 días de diferencia -- fuera de la ventana default (1 día), dentro de una
        // ventana configurada explícitamente en 5 días.
        var a = Movement(100m, BaseDate);
        var b = Movement(100m, BaseDate.AddDays(3));
        var options = new ReviewEngineOptions { DuplicateDetectionWindowDays = 5 };

        var withWiderWindow = CreateDetector(options).Detect([a, b]);
        var withDefaultWindow = CreateDetector().Detect([a, b]);

        Assert.Single(withWiderWindow);
        Assert.Empty(withDefaultWindow);
    }

    [Fact]
    public void Detect_VentanaTemporalConfigurada_SeRespetaEnSplits()
    {
        var total = Movement(1000m, BaseDate);
        var part1 = Movement(600m, BaseDate.AddDays(3));
        var part2 = Movement(400m, BaseDate.AddDays(3));
        var options = new ReviewEngineOptions { DuplicateDetectionWindowDays = 5 };

        var withWiderWindow = CreateDetector(options).Detect([total, part1, part2]);
        var withDefaultWindow = CreateDetector().Detect([total, part1, part2]);

        Assert.Single(withWiderWindow);
        Assert.Empty(withDefaultWindow);
    }

    [Fact]
    public void Detect_MasDeDoceCandidatosASplit_SoloConsideraLosPrimerosDoceEnOrdenDeEntrada()
    {
        // MaxSplitPartsConsidered (constante privada de SuspicionDetector) = 12: para
        // cada "total", solo se evalúan los primeros 12 candidatos a "parte" en el
        // orden en que aparecen en la lista de entrada (después de excluir al propio
        // total), sin importar cuántos más existan dentro de la ventana. Acá se
        // ubican 12 "señuelos" (que no combinan entre sí de ninguna forma cercana al
        // total) antes que las 2 partes que sí formarían un split válido -- el límite
        // hace que esas 2 últimas nunca lleguen a evaluarse.
        var total = Movement(1000m, description: "total");
        var decoys = Enumerable.Range(1, 12)
            .Select(i => Movement(5000m + i, description: $"decoy{i}"))
            .ToList();
        var validPart1 = Movement(500m, description: "validPart1");
        var validPart2 = Movement(500m, description: "validPart2");

        var inputMovements = new List<FinancialMovement> { total };
        inputMovements.AddRange(decoys);
        inputMovements.Add(validPart1);
        inputMovements.Add(validPart2);

        var groups = CreateDetector().Detect(inputMovements);

        Assert.Empty(groups.Where(g => g.Reason == SuspicionReason.SplitTransaction));
    }

    // ── Integración ──────────────────────────────────────────────────────────

    [Fact]
    public void Detect_CombinaDuplicadosYSplitsDetectadosEnLaMismaLista()
    {
        var dupA = Movement(200m, description: "dupA");
        var dupB = Movement(200m, description: "dupB");
        var total = Movement(9000m, description: "total");
        var part1 = Movement(5000m, description: "part1");
        var part2 = Movement(4000m, description: "part2");

        var groups = CreateDetector().Detect([dupA, dupB, total, part1, part2]);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Reason == SuspicionReason.PossibleDuplicate
            && g.Movements.Contains(dupA) && g.Movements.Contains(dupB));
        Assert.Contains(groups, g => g.Reason == SuspicionReason.SplitTransaction
            && g.Movements.Contains(total) && g.Movements.Contains(part1) && g.Movements.Contains(part2));
    }
}
