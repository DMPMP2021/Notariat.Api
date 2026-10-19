using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;
using Notariat.Api.Domain;

namespace Notariat.Api.Services;

public interface IEvaluationService
{
    decimal Tolerance { get; }
    Task<Evaluation> EvaluerAsync(int declarationId, decimal montantReference, string? observation, string verificateurId, string verificateurNom);
    Task<Evaluation> ReevaluerAsync(int declarationId, decimal montantReference, string? observation, string verificateurId, string verificateurNom);
}

public class EvaluationService : IEvaluationService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notif;
    private readonly IConfiguration _cfg;
    public EvaluationService(AppDbContext db, INotificationService notif, IConfiguration cfg)
    { _db = db; _notif = notif; _cfg = cfg; }

    /// <summary>Écart toléré (en %) entre le montant déclaré et la référence. Paramètre « Evaluation:TolerancePourcent » (5 par défaut).</summary>
    public decimal Tolerance => _cfg.GetValue<decimal>("Evaluation:TolerancePourcent", 5m);

    public async Task<Evaluation> EvaluerAsync(int declarationId, decimal montantReference, string? observation, string verificateurId, string verificateurNom)
    {
        if (montantReference <= 0) throw new ArgumentException("Le montant de référence doit être supérieur à 0.");
        var d = await _db.Declarations.Include(x => x.Evaluation).FirstOrDefaultAsync(x => x.Id == declarationId)
            ?? throw new KeyNotFoundException("Déclaration introuvable.");
        if (d.Statut != StatutDeclaration.Soumise)
            throw new InvalidOperationException($"Évaluation impossible : la déclaration est au statut « {d.Statut} ».");

        var ecart = Math.Round(d.PrixVente - montantReference, 2);
        var pct = Math.Round(ecart * 100m / montantReference, 2);
        var conclusion = Math.Abs(pct) <= Tolerance ? ConclusionEvaluation.Conforme
                       : ecart < 0 ? ConclusionEvaluation.SousEvalue : ConclusionEvaluation.SurEvalue;

        var ev = d.Evaluation ?? new Evaluation { DeclarationId = d.Id };
        if (d.Evaluation == null) _db.Evaluations.Add(ev);
        ev.VerificateurId = verificateurId; ev.VerificateurNom = verificateurNom;
        ev.MontantReference = montantReference; ev.MontantDeclare = d.PrixVente;
        ev.Ecart = ecart; ev.EcartPourcent = pct; ev.Conclusion = conclusion;
        ev.Observation = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
        ev.DateEvaluation = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var texte = conclusion switch
        {
            ConclusionEvaluation.Conforme => "montant conforme à la référence",
            ConclusionEvaluation.SousEvalue => $"montant SOUS-ÉVALUÉ (écart {pct} %)",
            _ => $"montant supérieur à la référence (écart +{pct} %)"
        };
        await _notif.NotifierRoleAsync(Roles.Inspecteur, $"Dossier {d.Reference} évalué par le vérificateur : {texte}", d.Id);
        return ev;
    }

    /// <summary>Réévaluation post-enregistrement : le vérificateur contrôle l'acte après son enregistrement et décide d'ajuster la référence.</summary>
    public async Task<Evaluation> ReevaluerAsync(int declarationId, decimal montantReference, string? observation, string verificateurId, string verificateurNom)
    {
        if (montantReference <= 0) throw new ArgumentException("Le montant de référence doit être supérieur à 0.");
        var d = await _db.Declarations.Include(x => x.Evaluation).FirstOrDefaultAsync(x => x.Id == declarationId)
            ?? throw new KeyNotFoundException("Déclaration introuvable.");
        if (d.Statut != StatutDeclaration.Enregistree)
            throw new InvalidOperationException($"Réévaluation impossible : la déclaration est au statut « {d.Statut} ».");

        var ecart = Math.Round(d.MontantRetenu - montantReference, 2);
        var pct = montantReference != 0 ? Math.Round(ecart * 100m / montantReference, 2) : 0;
        var conclusion = Math.Abs(pct) <= Tolerance ? ConclusionEvaluation.Conforme
                       : ecart < 0 ? ConclusionEvaluation.SousEvalue : ConclusionEvaluation.SurEvalue;

        var ev = d.Evaluation ?? new Evaluation { DeclarationId = d.Id };
        if (d.Evaluation == null) _db.Evaluations.Add(ev);
        ev.VerificateurId = verificateurId; ev.VerificateurNom = verificateurNom;
        ev.MontantReference = montantReference; ev.MontantDeclare = d.MontantRetenu;
        ev.Ecart = ecart; ev.EcartPourcent = pct; ev.Conclusion = conclusion;
        ev.Observation = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
        ev.DateEvaluation = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var texte = conclusion switch
        {
            ConclusionEvaluation.Conforme => "montant conforme à la référence (réévaluation)",
            ConclusionEvaluation.SousEvalue => $"montant SOUS-ÉVALUÉ après enregistrement (écart {pct} %)",
            _ => $"montant supérieur à la référence après enregistrement (écart +{pct} %)"
        };
        await _notif.NotifierRoleAsync(Roles.Inspecteur, $"Dossier {d.Reference} réévalué par le vérificateur : {texte}", d.Id);
        return ev;
    }
}
