using System.Text.Json;
using ClaimLens.Api.Data;
using ClaimLens.Api.Domain;
using ClaimLens.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Base de données : SQLite par défaut, SQL Server en changeant Database:Provider.
builder.Services.AddDbContext<ClaimsDb>(o =>
{
    if (config["Database:Provider"] == "SqlServer")
        o.UseSqlServer(config.GetConnectionString("SqlServer"));
    else
        o.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=claimlens.db");
});

builder.Services.AddHttpClient<OllamaAnalyzer>(c =>
{
    c.BaseAddress = new Uri(config["Llm:Url"] ?? "http://localhost:11434");
    c.Timeout = TimeSpan.FromSeconds(int.TryParse(config["Llm:TimeoutSeconds"], out var s) ? s : 60);
});
builder.Services.AddSingleton<RuleBasedAnalyzer>();
builder.Services.AddScoped<ResilientAnalyzer>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<ClaimsDb>().Database.EnsureCreated();

app.UseCors();
app.UseOpenApi();
app.UseSwaggerUi();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/api/health", (IConfiguration c) =>
    Results.Ok(new { status = "ok", model = c["Llm:Model"] ?? "qwen2.5:3b", database = c["Database:Provider"] ?? "Sqlite" }));

app.MapPost("/api/claims", async (ClaimRequest req, ResilientAnalyzer analyzer, ClaimsDb db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Declaration) || req.Declaration.Length > 8000)
        return Results.BadRequest(new { error = "La déclaration doit contenir entre 1 et 8 000 caractères." });

    var analysis = await analyzer.AnalyzeAsync(req.Declaration, ct);
    var claim = new Claim
    {
        Declaration = req.Declaration,
        TypeSinistre = analysis.TypeSinistre,
        Urgence = analysis.Urgence,
        Statut = analysis.Statut,
        TauxAncrage = analysis.TauxAncrage,
        AnalyseJson = JsonSerializer.Serialize(analysis, json),
    };
    db.Claims.Add(claim);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { id = claim.Id, creeLe = claim.CreeLe, analysis });
});

app.MapGet("/api/claims", async (ClaimsDb db, CancellationToken ct) =>
    Results.Ok(await db.Claims.OrderByDescending(c => c.CreeLe).Take(50)
        .Select(c => new { c.Id, c.CreeLe, c.TypeSinistre, c.Urgence, c.Statut, c.TauxAncrage })
        .ToListAsync(ct)));

app.MapGet("/api/claims/{id:int}", async (int id, ClaimsDb db, CancellationToken ct) =>
    await db.Claims.FindAsync([id], ct) is { } c
        ? Results.Ok(new { c.Id, c.CreeLe, c.Declaration, analysis = JsonSerializer.Deserialize<Analysis>(c.AnalyseJson, json) })
        : Results.NotFound());

app.Run();

public partial class Program { }
