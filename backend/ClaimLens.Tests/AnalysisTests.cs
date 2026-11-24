using ClaimLens.Api.Domain;
using ClaimLens.Api.Services;
using Xunit;

namespace ClaimLens.Tests;

public class EvidenceVerifierTests
{
    private const string Declaration =
        "Le 12 novembre 2026, ma voiture a été percutée à l'arrière sur l'avenue de la Liberté à Luxembourg. " +
        "Le conducteur adverse a reconnu sa faute. Pare-chocs arrière enfoncé. Contrat n° AUTO-458812.";

    [Fact]
    public void Citation_copiee_du_texte_est_verifiee_malgre_accents_et_casse()
    {
        var a = new Analysis("Automobile", "Basse", "", [new ExtractedFact("Lieu", "Luxembourg", "AVENUE DE LA LIBERTE")], [], "test");
        Assert.True(EvidenceVerifier.Verify(a, Declaration).Faits[0].Verifie);
    }

    [Fact]
    public void Citation_inventee_est_bloquee_et_le_dossier_part_en_relecture()
    {
        var a = new Analysis("Automobile", "Haute", "", [
            new ExtractedFact("Date", "12/11/2026", "Le 12 novembre 2026"),
            new ExtractedFact("Blessés", "1 blessé", "le passager a été hospitalisé"),
        ], [], "test");
        var v = EvidenceVerifier.Verify(a, Declaration);
        Assert.True(v.Faits[0].Verifie);
        Assert.False(v.Faits[1].Verifie);
        Assert.Equal("À relire", v.Statut);
        Assert.Equal(50.0, v.TauxAncrage);
    }

    [Fact]
    public void Citation_trop_courte_n_est_pas_acceptee()
    {
        var a = new Analysis("Autre", "Basse", "", [new ExtractedFact("Lieu", "?", "à")], [], "test");
        Assert.False(EvidenceVerifier.Verify(a, Declaration).Faits[0].Verifie);
    }
}

public class AnalyzerTests
{
    [Fact]
    public async Task Moteur_de_regles_classe_un_accident_auto_et_demande_le_constat()
    {
        var text = "Le 3 octobre 2026, accrochage avec un autre véhicule rue Pasteur à Strassen. Contrat n° AUTO-112233.";
        var a = EvidenceVerifier.Verify(await new RuleBasedAnalyzer().AnalyzeAsync(text, default), text);
        Assert.Equal("Automobile", a.TypeSinistre);
        Assert.Contains("Constat amiable signé", a.PiecesManquantes);
        Assert.All(a.Faits, f => Assert.True(f.Verifie));
        Assert.Equal("Prêt pour le gestionnaire", a.Statut);
    }

    [Fact]
    public async Task Blesse_donne_une_urgence_haute()
    {
        var text = "Incendie dans la cuisine du logement le 2 mars 2026, un enfant blessé conduit aux urgences.";
        var a = await new RuleBasedAnalyzer().AnalyzeAsync(text, default);
        Assert.Equal("Habitation", a.TypeSinistre);
        Assert.Equal("Haute", a.Urgence);
        Assert.Contains("Certificat médical", a.PiecesManquantes);
    }

    [Fact]
    public void Reponse_json_du_llm_est_lue_meme_incomplete()
    {
        var a = LlmJson.Parse("""{"typeSinistre":"Habitation","faits":[{"champ":"Date","citation":"le 2 mars"}]}""", "llm");
        Assert.Equal("Habitation", a.TypeSinistre);
        Assert.Equal("", a.Urgence);
        Assert.Single(a.Faits);
        Assert.Empty(a.PiecesManquantes);
    }
}
