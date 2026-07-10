using System.Diagnostics;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;

namespace FinancialSystem.Infrastructure.Review;

/// <summary>
/// Orquesta <see cref="IMovementLoader"/> e <see cref="ISuspicionDetector"/> para un
/// período: carga los movimientos y detecta grupos sospechosos entre ellos.
///
/// PR-L4: hasta acá también orquestaba IMatchScorer para sugerir coincidencias contra
/// movimientos "Candidate" (legacy Excel) — ese mecanismo se retiró completo junto con
/// el soporte Legacy (ver ReviewResult.cs para el detalle). Sin candidatos no hay nada
/// contra qué emparejar banco/tarjeta, así que ya no tiene sentido separar la lista
/// cargada en dos lados — se detectan sospechosos sobre la lista completa, una sola vez.
/// </summary>
internal sealed class ReviewEngine : IReviewEngine
{
    private readonly IMovementLoader _movementLoader;
    private readonly ISuspicionDetector _suspicionDetector;

    public ReviewEngine(IMovementLoader movementLoader, ISuspicionDetector suspicionDetector)
    {
        _movementLoader = movementLoader;
        _suspicionDetector = suspicionDetector;
    }

    public async Task<ReviewResult> GenerateAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var movements = await _movementLoader.LoadAsync(from, to, cancellationToken);
        var suspicious = _suspicionDetector.Detect(movements);

        stopwatch.Stop();

        return new ReviewResult
        {
            PeriodStart = from,
            PeriodEnd = to,
            Movements = movements,
            Suspicious = suspicious,
            Elapsed = stopwatch.Elapsed,
        };
    }
}
