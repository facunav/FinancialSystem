using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FinancialSystem.Infrastructure.Tests")]
// Patch 0108: permite que MovementLookupServiceRealEfRegressionTests (McpServer.Tests)
// construya MovementLookupService (internal) directamente contra un AppDbContext real
// (InMemory), en vez de un fake de IMovementLookupService -- necesario para reproducir el
// comportamiento real de EF Core en GetMovement/ExplainMovement/ExplainClassification, los
// 3 consumidores de MovementTools que dependen de MovementLookupService.
[assembly: InternalsVisibleTo("FinancialSystem.McpServer.Tests")]
