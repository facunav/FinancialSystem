using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FinancialSystem.Infrastructure.Tests")]
// Patch 0108: permite que MovementLookupServiceRealEfRegressionTests (McpServer.Tests)
// construya MovementLookupService (internal) directamente contra un AppDbContext real
// (InMemory), en vez de un fake de IMovementLookupService -- necesario para reproducir el
// comportamiento real de EF Core en GetMovement/ExplainMovement/ExplainClassification, los
// 3 consumidores de MovementTools que dependen de MovementLookupService.
[assembly: InternalsVisibleTo("FinancialSystem.McpServer.Tests")]
// Patch 0112: permite que DedupeEndpointsTests (FinancialMcp.Api.Tests) construya
// DedupeEngine (internal) directamente contra un AppDbContext real (InMemory) para
// probar POST /api/dedupe/apply de punta a punta con el motor real -- mismo criterio y
// mismo motivo que el InternalsVisibleTo de arriba para McpServer.Tests (necesario para
// reproducir el comportamiento real del motor, no un fake de IDedupeEngine, en los
// casos que dependen de las reglas reales de Evaluate/vía B).
[assembly: InternalsVisibleTo("FinancialMcp.Api.Tests")]
