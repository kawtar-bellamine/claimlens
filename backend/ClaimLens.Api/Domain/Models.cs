namespace ClaimLens.Api.Domain;

/// <summary>Un fait extrait de la déclaration, avec la citation exacte qui le justifie.</summary>
public sealed record ExtractedFact(string Champ, string Valeur, string Citation)
{
    /// <summary>Vrai si la citation a été retrouvée telle quelle dans la déclaration.</summary>
    public bool Verifie { get; init; }
}

/// <summary>Résultat d'analyse renvoyé au Front-End.</summary>
public sealed record Analysis(
    string TypeSinistre,
    string Urgence,
    string Resume,
    IReadOnlyList<ExtractedFact> Faits,
    IReadOnlyList<string> PiecesManquantes,
    string Moteur)
{
    public int FaitsVerifies => Faits.Count(f => f.Verifie);
    public double TauxAncrage => Faits.Count == 0 ? 0 : Math.Round(100.0 * FaitsVerifies / Faits.Count, 1);

    /// <summary>« Prêt » seulement si chaque fait est ancré dans le texte ; sinon un gestionnaire relit.</summary>
    public string Statut => Faits.Count > 0 && FaitsVerifies == Faits.Count ? "Prêt pour le gestionnaire" : "À relire";
}

/// <summary>Déclaration stockée en base (SQLite en local, SQL Server en configuration entreprise).</summary>
public sealed class Claim
{
    public int Id { get; set; }
    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public string Declaration { get; set; } = "";
    public string TypeSinistre { get; set; } = "";
    public string Urgence { get; set; } = "";
    public string Statut { get; set; } = "";
    public double TauxAncrage { get; set; }
    public string AnalyseJson { get; set; } = "";
}

public sealed record ClaimRequest(string Declaration);
