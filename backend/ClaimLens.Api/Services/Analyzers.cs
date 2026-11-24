using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaimLens.Api.Domain;

namespace ClaimLens.Api.Services;

public interface IClaimAnalyzer
{
    string Name { get; }
    Task<Analysis> AnalyzeAsync(string declaration, CancellationToken ct);
}

/// <summary>LLM exécuté localement avec Ollama : aucune donnée d'assuré ne quitte le poste.</summary>
public sealed class OllamaAnalyzer(HttpClient http, IConfiguration config) : IClaimAnalyzer
{
    private readonly string _model = config["Llm:Model"] ?? "qwen2.5:3b";
    public string Name => $"LLM local ({_model})";

    public const string SystemPrompt = """
        Tu es un assistant de gestion de sinistres pour un assureur.
        Analyse la déclaration et réponds UNIQUEMENT avec un objet JSON :
        {"typeSinistre": "Automobile|Habitation|Santé|Responsabilité civile|Autre",
         "urgence": "Haute|Moyenne|Basse",
         "resume": "deux phrases maximum",
         "faits": [{"champ": "Date|Lieu|Tiers|Dommages|Blessés|Police", "valeur": "...", "citation": "extrait copié mot pour mot de la déclaration"}],
         "piecesManquantes": ["..."]}
        Chaque citation doit être copiée exactement depuis la déclaration. N'invente aucune information.
        """;

    public async Task<Analysis> AnalyzeAsync(string declaration, CancellationToken ct)
    {
        var body = new
        {
            model = _model,
            stream = false,
            format = "json",
            options = new { temperature = 0 },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = declaration },
            },
        };
        using var response = await http.PostAsJsonAsync("/api/chat", body, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
        return LlmJson.Parse(content, Name);
    }
}

/// <summary>Lecture tolérante de la réponse du LLM (champs manquants remplacés par des valeurs vides).</summary>
public static class LlmJson
{
    public static Analysis Parse(string json, string engine)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var faits = new List<ExtractedFact>();
        if (root.TryGetProperty("faits", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var f in arr.EnumerateArray())
                faits.Add(new ExtractedFact(Str(f, "champ"), Str(f, "valeur"), Str(f, "citation")));

        var pieces = new List<string>();
        if (root.TryGetProperty("piecesManquantes", out var p) && p.ValueKind == JsonValueKind.Array)
            foreach (var x in p.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) pieces.Add(x.GetString() ?? "");

        return new Analysis(Str(root, "typeSinistre"), Str(root, "urgence"), Str(root, "resume"), faits, pieces, engine);
    }
}

/// <summary>Moteur de secours par règles, utilisé quand Ollama n'est pas lancé (démo et tests).</summary>
public sealed partial class RuleBasedAnalyzer : IClaimAnalyzer
{
    public string Name => "Règles (secours sans LLM)";

    [GeneratedRegex(@"\b(le|du)\s+\d{1,2}\s+(janvier|février|fevrier|mars|avril|mai|juin|juillet|août|aout|septembre|octobre|novembre|décembre|decembre)(\s+\d{4})?", RegexOptions.IgnoreCase)]
    private static partial Regex DateRx();

    [GeneratedRegex(@"(?<=^|\s)(à|sur|rue|avenue|boulevard)\s+[A-ZÉÈ][\wÀ-ÿ'\- ]{2,40}")]
    private static partial Regex PlaceRx();

    [GeneratedRegex(@"(contrat|police)\s*(n°|no|numéro)?\s*[A-Z0-9\-]{5,}", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyRx();

    public Task<Analysis> AnalyzeAsync(string declaration, CancellationToken ct)
    {
        var t = declaration.ToLowerInvariant();
        var type = Has(t, "voiture", "véhicule", "vehicule", "accrochage", "collision", "pare-chocs", "conducteur")
            ? "Automobile"
            : Has(t, "dégât des eaux", "degat des eaux", "fuite", "incendie", "cambriolage", "logement", "appartement")
                ? "Habitation"
                : Has(t, "hôpital", "hopital", "médecin", "medecin", "blessure") ? "Santé" : "Autre";
        var blesse = Has(t, "blessé", "blesse", "urgences", "hôpital", "hopital");
        var urgence = blesse || Has(t, "incendie", "inhabitable") ? "Haute" : Has(t, "fuite", "bloqué", "bloque") ? "Moyenne" : "Basse";

        var faits = new List<ExtractedFact>();
        void Add(string champ, Match m)
        {
            if (m.Success) faits.Add(new ExtractedFact(champ, m.Value.Trim(), m.Value.Trim()));
        }
        Add("Date", DateRx().Match(declaration));
        Add("Lieu", PlaceRx().Match(declaration));
        Add("Police", PolicyRx().Match(declaration));

        var pieces = new List<string>();
        if (type == "Automobile" && !Has(t, "constat")) pieces.Add("Constat amiable signé");
        if (!Has(t, "photo")) pieces.Add("Photos des dommages");
        if (type == "Habitation" && !Has(t, "facture", "devis")) pieces.Add("Devis ou factures de réparation");
        if (blesse && !Has(t, "certificat")) pieces.Add("Certificat médical");

        var resume = $"Sinistre {type.ToLowerInvariant()} déclaré, urgence {urgence.ToLowerInvariant()}.";
        return Task.FromResult(new Analysis(type, urgence, resume, faits, pieces, Name));
    }

    private static bool Has(string text, params string[] words) => words.Any(w => text.Contains(w));
}

/// <summary>Essaie le LLM local, bascule sur les règles s'il ne répond pas.</summary>
public sealed class ResilientAnalyzer(OllamaAnalyzer llm, RuleBasedAnalyzer rules, ILogger<ResilientAnalyzer> log)
{
    public async Task<Analysis> AnalyzeAsync(string declaration, CancellationToken ct)
    {
        Analysis raw;
        try
        {
            raw = await llm.AnalyzeAsync(declaration, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogWarning("LLM local indisponible ({Message}) : moteur de règles utilisé.", ex.Message);
            raw = await rules.AnalyzeAsync(declaration, ct);
        }
        return EvidenceVerifier.Verify(raw, declaration);
    }
}
