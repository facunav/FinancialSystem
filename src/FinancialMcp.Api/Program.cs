using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Application;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();

//  Infraestructura compartida (DB, parsers, importers) 
builder.Services.AddInfrastructure(builder.Configuration);

//  Motor y servicios de conciliacion 
builder.Services.AddReconciliation(builder.Configuration);

//  Servicio de rehidratacion (nuevo, especifico de la API)
builder.Services.AddScoped<MovementHydrationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();

}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();

app.MapGet("/", () => Results.Redirect("/group-reconciliation.html"));

app.MapReconciliationEndpoints();

app.Run();
