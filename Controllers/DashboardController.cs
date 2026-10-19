using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;
using Notariat.Api.Domain;

namespace Notariat.Api.Controllers;

public record KpiDto(string Key, string Label, decimal Value, string Format, string? Hint = null, string? Tone = null);
public record SerieDto(string Name, decimal[] Values);
public record StatutCountDto(string Statut, int Count);
public record DashboardDto(string Role, List<KpiDto> Kpis, List<StatutCountDto> Statuts,
                           string SeriesTitle, string SeriesFormat, List<string> Labels, List<SerieDto> Series);

/// <summary>Un seul endpoint : le contenu dépend du rôle de l'utilisateur connecté.</summary>
[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (User.IsInRole(Roles.Notaire)) return Ok(await Notaire());
        if (User.IsInRole(Roles.Inspecteur)) return Ok(await Inspecteur());

        if (User.IsInRole(Roles.Receveur)) return Ok(await Receveur());
        if (User.IsInRole(Roles.Verificateur)) return Ok(await Verificateur());

        return Forbid();
    }

    // ---------- helpers ----------
    private static List<DateTime> DerniersMois(int n = 6)
    {
        var now = DateTime.UtcNow;
        var d0 = new DateTime(now.Year, now.Month, 1);
        return Enumerable.Range(0, n).Select(i => d0.AddMonths(i - (n - 1))).ToList();
    }
    private static decimal[] ParMois(IEnumerable<DateTime> dates, List<DateTime> mois) =>
        mois.Select(m => (decimal)dates.Count(x => x.Year == m.Year && x.Month == m.Month)).ToArray();
    private static decimal[] SommeParMois(IEnumerable<(DateTime date, decimal v)> l, List<DateTime> mois) =>
        mois.Select(m => l.Where(x => x.date.Year == m.Year && x.date.Month == m.Month).Sum(x => x.v)).ToArray();
    private static decimal Pct(int a, int b) => b > 0 ? Math.Round(a * 100m / b, 1) : 0;
    private static List<StatutCountDto> Repartition(IEnumerable<Declaration> l) =>
        Enum.GetValues<StatutDeclaration>()
            .Select(s => new StatutCountDto(s.ToString(), l.Count(d => d.Statut == s))).ToList();

    // ---------- NOTAIRE ----------
    private async Task<DashboardDto> Notaire()
    {
        var decls = await _db.Declarations.AsNoTracking()
            .Include(d => d.OrdreVersement).Include(d => d.Historique)
            .Where(d => d.NotaireId == UserId).ToListAsync();

        int Nb(StatutDeclaration s) => decls.Count(d => d.Statut == s);
        var soumises = decls.Count(d => d.Historique.Any(h => h.Statut == StatutDeclaration.Soumise));
        var rejetees = decls.Count(d => d.Historique.Any(h => h.Statut == StatutDeclaration.Rejetee));
        var delais = decls.Where(d => d.DateEnregistrement != null && d.Historique.Any(h => h.Statut == StatutDeclaration.Soumise))
            .Select(d => (d.DateEnregistrement!.Value - d.Historique.Where(h => h.Statut == StatutDeclaration.Soumise).Min(h => h.Date)).TotalDays)
            .ToList();
        var aPayer = decls.Where(d => d.Statut == StatutDeclaration.InstancePaiement).Sum(d => d.OrdreVersement?.Montant ?? 0);
        var paye = decls.Where(d => d.Statut is StatutDeclaration.Payee or StatutDeclaration.Enregistree).Sum(d => d.OrdreVersement?.Montant ?? 0);

        var mois = DerniersMois();
        var kpis = new List<KpiDto>
        {
            new("total", "Déclarations déposées", decls.Count, "number"),
            new("rejetees", "À corriger (rejetées)", Nb(StatutDeclaration.Rejetee), "number", "Corrigez puis renvoyez", Nb(StatutDeclaration.Rejetee) > 0 ? "danger" : null),
            new("attente", "En attente de l'inspecteur", Nb(StatutDeclaration.Soumise), "number", null, Nb(StatutDeclaration.Soumise) > 0 ? "warn" : null),
            new("aPayer", "Ordres à payer", Nb(StatutDeclaration.InstancePaiement), "number", "Présentez-les au Receveur", Nb(StatutDeclaration.InstancePaiement) > 0 ? "warn" : null),
            new("montantAPayer", "Montant à payer", aPayer, "money", null, aPayer > 0 ? "warn" : null),
            new("paye", "Droits payés", paye, "money", null, "ok"),
            new("enregistrees", "Actes enregistrés", Nb(StatutDeclaration.Enregistree), "number", null, "ok"),
            new("tauxRejet", "Taux de rejet", Pct(rejetees, soumises), "percent", $"{rejetees} sur {soumises} soumises"),
            new("delai", "Délai moyen d'enregistrement", delais.Any() ? Math.Round((decimal)delais.Average(), 1) : 0, "days", "De l'envoi à l'enregistrement"),
        };
        return new DashboardDto(Roles.Notaire, kpis, Repartition(decls), "Activité des 6 derniers mois", "number",
            mois.Select(m => m.ToString("MM/yyyy")).ToList(),
            new List<SerieDto>
            {
                new("Déclarations créées", ParMois(decls.SelectMany(d => d.Historique).Where(h => h.Statut == StatutDeclaration.Brouillon).Select(h => h.Date), mois)),
                new("Actes enregistrés", ParMois(decls.Where(d => d.DateEnregistrement != null).Select(d => d.DateEnregistrement!.Value), mois)),
            });
    }

    // ---------- INSPECTEUR ----------
    private async Task<DashboardDto> Inspecteur()
    {
        var decls = await _db.Declarations.AsNoTracking()
            .Include(d => d.OrdreVersement).Include(d => d.Historique).ToListAsync();
        var hist = decls.SelectMany(d => d.Historique).ToList();
        var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var mesValid = hist.Where(h => h.UserId == UserId && h.Statut == StatutDeclaration.InstancePaiement).ToList();
        var mesRejets = hist.Where(h => h.UserId == UserId && h.Statut == StatutDeclaration.Rejetee).ToList();
        var mesEnreg = hist.Where(h => h.UserId == UserId && h.Statut == StatutDeclaration.Enregistree).ToList();

        // délai de traitement : de chaque soumission à la décision suivante
        var delais = new List<double>();
        foreach (var d in decls)
        {
            var h = d.Historique.OrderBy(x => x.Date).ToList();
            for (var i = 0; i < h.Count; i++)
            {
                if (h[i].Statut != StatutDeclaration.Soumise) continue;
                var fin = h.Skip(i + 1).FirstOrDefault(x => x.Statut is StatutDeclaration.InstancePaiement or StatutDeclaration.Rejetee);
                if (fin != null) delais.Add((fin.Date - h[i].Date).TotalHours);
            }
        }
        var liquide = decls.Where(d => d.OrdreVersement != null && d.OrdreVersement.DateGeneration >= debutMois)
            .Sum(d => d.OrdreVersement!.Montant);
        int Nb(StatutDeclaration s) => decls.Count(d => d.Statut == s);

        var mois = DerniersMois();
        var kpis = new List<KpiDto>
        {
            new("aVerifier", "Déclarations à vérifier", Nb(StatutDeclaration.Soumise), "number", "File d'attente", Nb(StatutDeclaration.Soumise) > 0 ? "warn" : "ok"),
            new("aEnregistrer", "Actes payés à enregistrer", Nb(StatutDeclaration.Payee), "number", "File d'attente", Nb(StatutDeclaration.Payee) > 0 ? "warn" : "ok"),
            new("attentePaiement", "En attente de paiement", Nb(StatutDeclaration.InstancePaiement), "number", "Chez le notaire / receveur", "info"),
            new("validees", "Validées ce mois", mesValid.Count(h => h.Date >= debutMois), "number", "Par vous"),
            new("rejetees", "Rejetées ce mois", mesRejets.Count(h => h.Date >= debutMois), "number", "Par vous"),
            new("enregistrees", "Enregistrées ce mois", mesEnreg.Count(h => h.Date >= debutMois), "number", "Par vous", "ok"),
            new("liquide", "Droits liquidés ce mois", liquide, "money", "Ordres de versement générés", "ok"),
            new("tauxRejet", "Votre taux de rejet", Pct(mesRejets.Count, mesRejets.Count + mesValid.Count), "percent"),
            new("delai", "Délai moyen de traitement", delais.Any() ? Math.Round((decimal)delais.Average(), 1) : 0, "hours", "De la soumission à la décision"),
        };
        return new DashboardDto(Roles.Inspecteur, kpis, Repartition(decls), "Vos décisions des 6 derniers mois", "number",
            mois.Select(m => m.ToString("MM/yyyy")).ToList(),
            new List<SerieDto>
            {
                new("Validées", ParMois(mesValid.Select(h => h.Date), mois)),
                new("Rejetées", ParMois(mesRejets.Select(h => h.Date), mois)),
                new("Enregistrées", ParMois(mesEnreg.Select(h => h.Date), mois)),
            });
    }
    // ---------- VÉRIFICATEUR ----------
    private async Task<DashboardDto> Verificateur()
    {
        var mes = await _db.Evaluations.AsNoTracking().Where(e => e.VerificateurId == UserId).ToListAsync();
        var aEvaluer = await _db.Declarations.CountAsync(d => d.Statut == StatutDeclaration.Soumise && d.Evaluation == null);
        var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var sous = mes.Where(e => e.Conclusion == ConclusionEvaluation.SousEvalue).ToList();
        var mois = DerniersMois();
        var kpis = new List<KpiDto>
        {
            new("aEvaluer", "Dossiers à évaluer", aEvaluer, "number", "File d'attente", aEvaluer > 0 ? "warn" : "ok"),
            new("ceMois", "Évalués ce mois", mes.Count(e => e.DateEvaluation >= debutMois), "number", "Par vous"),
            new("conformes", "Conformes", mes.Count(e => e.Conclusion == ConclusionEvaluation.Conforme), "number", "Montant conforme à la référence", "ok"),
            new("sous", "Sous-évalués", sous.Count, "number", "Déclaré < référence", sous.Any() ? "danger" : null),
            new("sur", "Supérieurs à la référence", mes.Count(e => e.Conclusion == ConclusionEvaluation.SurEvalue), "number", null, "info"),
            new("insuffisance", "Insuffisance totale relevée", sous.Sum(e => -e.Ecart), "money", "Somme des écarts des actes sous-évalués", sous.Any() ? "danger" : null),
            new("ecartMoyen", "Écart moyen (valeur absolue)", mes.Any() ? Math.Round(mes.Average(e => Math.Abs(e.EcartPourcent)), 1) : 0, "percent"),
        };
        return new DashboardDto(Roles.Verificateur, kpis, new List<StatutCountDto>(), "Vos évaluations des 6 derniers mois", "number",
            mois.Select(m => m.ToString("MM/yyyy")).ToList(),
            new List<SerieDto>
            {
                new("Conformes", ParMois(mes.Where(e => e.Conclusion == ConclusionEvaluation.Conforme).Select(e => e.DateEvaluation), mois)),
                new("Sous-évalués", ParMois(sous.Select(e => e.DateEvaluation), mois)),
                new("Supérieurs", ParMois(mes.Where(e => e.Conclusion == ConclusionEvaluation.SurEvalue).Select(e => e.DateEvaluation), mois)),
            });
    }

    // ---------- RECEVEUR ----------
    private async Task<DashboardDto> Receveur()
    {
        var ordres = await _db.OrdresVersement.AsNoTracking()
            .Include(o => o.Declaration).Include(o => o.Paiement).ToListAsync();
        var now = DateTime.UtcNow;
        var aujourdhui = now.Date;
        var debutMois = new DateTime(now.Year, now.Month, 1);

        var attente = ordres.Where(o => !o.EstPaye).ToList();
        var mes = ordres.Where(o => o.Paiement != null && o.Paiement.ReceveurId == UserId).ToList();
        var jour = mes.Where(o => o.Paiement!.DatePaiement.Date == aujourdhui).ToList();
        var moisCourant = mes.Where(o => o.Paiement!.DatePaiement >= debutMois).ToList();

        var mois = DerniersMois();
        var kpis = new List<KpiDto>
        {
            new("aEncaisser", "Ordres à encaisser", attente.Count, "number", "File d'attente", attente.Any() ? "warn" : "ok"),
            new("montantAEncaisser", "Montant à encaisser", attente.Sum(o => o.Montant), "money", null, attente.Any() ? "warn" : null),
            new("quittancesJour", "Quittances délivrées aujourd'hui", jour.Count, "number", "Par vous"),
            new("encaisseJour", "Encaissé aujourd'hui", jour.Sum(o => o.Montant), "money", null, "ok"),
            new("quittancesMois", "Quittances délivrées ce mois", moisCourant.Count, "number", "Par vous"),
            new("encaisseMois", "Encaissé ce mois", moisCourant.Sum(o => o.Montant), "money", null, "ok"),
            new("penalites", "dont pénalités de retard (mois)", moisCourant.Sum(o => o.Declaration.Penalite), "money", null, "info"),
            new("total", "Total encaissé", mes.Sum(o => o.Montant), "money", "Depuis le début", "ok"),
        };
        return new DashboardDto(Roles.Receveur, kpis, new List<StatutCountDto>(), "Encaissements des 6 derniers mois (DA)", "money",
            mois.Select(m => m.ToString("MM/yyyy")).ToList(),
            new List<SerieDto> { new("Montant encaissé", SommeParMois(mes.Select(o => (o.Paiement!.DatePaiement, o.Montant)), mois)) });
    }
}
