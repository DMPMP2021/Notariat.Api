using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;
using Notariat.Api.Domain;

namespace Notariat.Api.Services;

public interface IDeclarationService
{
    Task<Declaration> CreerAsync(DeclarationDto dto, string notaireId);
    Task<Declaration> ModifierAsync(int id, DeclarationDto dto, string notaireId);
    Task SoumettreAsync(int id, string notaireId);
    Task ValiderAsync(int id, string codeQ, bool utiliserReference, string inspecteurId);
    Task RejeterAsync(int id, string motif, string inspecteurId);
    Task<Paiement> ConfirmerPaiementAsync(int ordreId, string numeroQuittance, string? fichier, string receveurId);
    Task<Paiement> ModifierQuittanceAsync(int ordreId, string numeroQuittance, string? fichier, string receveurId);
    Task<Declaration> EnregistrerAsync(int id, string inspecteurId);
}

public class DeclarationService : IDeclarationService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notif;
    private readonly IDroitsService _droits;
    private readonly IConfiguration _cfg;
    public DeclarationService(AppDbContext db, INotificationService notif, IDroitsService droits, IConfiguration cfg)
    { _db = db; _notif = notif; _droits = droits; _cfg = cfg; }

    private static readonly Regex FichierOk = new(@"^[0-9a-fA-F-]{36}\.(pdf|doc|docx)$");
    private static void ControlerFichier(string? nom)
    {
        if (!string.IsNullOrEmpty(nom) && !FichierOk.IsMatch(nom))
            throw new ArgumentException("Nom de fichier invalide : utilisez le fichier renvoyé par l'upload.");
    }

    public async Task<Declaration> CreerAsync(DeclarationDto dto, string notaireId)
    {
        ControlerFichier(dto.CheminFichier);
        var d = new Declaration
        {
            Reference = $"DEC-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            NotaireId = notaireId,
            VendeurNom = dto.VendeurNom,
            AcheteurNom = dto.AcheteurNom,
            DesignationBien = dto.DesignationBien,
            PrixVente = dto.PrixVente,
            DateActe = dto.DateActe,
            CheminFichier = dto.CheminFichier   // la catégorie et le détail (CodeQ) seront choisis par l'inspecteur
        };
        d.Historique.Add(new HistoriqueStatut { Statut = StatutDeclaration.Brouillon, UserId = notaireId });
        _db.Declarations.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    public async Task<Declaration> ModifierAsync(int id, DeclarationDto dto, string notaireId)
    {
        var d = await Get(id);
        if (d.NotaireId != notaireId) throw new UnauthorizedAccessException();
        Verifier(d, StatutDeclaration.Brouillon, StatutDeclaration.Rejetee);
        d.VendeurNom = dto.VendeurNom;
        d.AcheteurNom = dto.AcheteurNom;
        ControlerFichier(dto.CheminFichier);
        d.DesignationBien = dto.DesignationBien;
        d.PrixVente = dto.PrixVente;
        d.DateActe = dto.DateActe;
        d.CheminFichier = dto.CheminFichier ?? d.CheminFichier;
        await _db.SaveChangesAsync();
        return d;
    }

    public async Task SoumettreAsync(int id, string notaireId)
    {
        var d = await Get(id);
        if (d.NotaireId != notaireId) throw new UnauthorizedAccessException();
        Verifier(d, StatutDeclaration.Brouillon, StatutDeclaration.Rejetee);
        Changer(d, StatutDeclaration.Soumise, notaireId);
        d.MotifRejet = null;
        // une déclaration corrigée puis renvoyée doit être réévaluée
        var ancienne = await _db.Evaluations.FirstOrDefaultAsync(e => e.DeclarationId == d.Id);
        if (ancienne != null) _db.Evaluations.Remove(ancienne);
        await _db.SaveChangesAsync();
        await _notif.NotifierRoleAsync(Roles.Inspecteur, $"Nouvelle déclaration {d.Reference} à vérifier", d.Id);
        await _notif.NotifierRoleAsync(Roles.Verificateur, $"Nouvelle déclaration {d.Reference} à évaluer", d.Id);
    }

    public async Task RejeterAsync(int id, string motif, string inspecteurId)
    {
        if (string.IsNullOrWhiteSpace(motif)) throw new ArgumentException("Le motif de rejet est obligatoire.");
        var d = await Get(id);
        Verifier(d, StatutDeclaration.Soumise);
        d.MotifRejet = motif;
        Changer(d, StatutDeclaration.Rejetee, inspecteurId, motif);
        await _db.SaveChangesAsync();
        await _notif.NotifierAsync(d.NotaireId, $"Déclaration {d.Reference} rejetée : {motif}", d.Id);
    }

    public async Task ValiderAsync(int id, string codeQ, bool utiliserReference, string inspecteurId)
    {
        if (string.IsNullOrWhiteSpace(codeQ)) throw new ArgumentException("Choisissez la catégorie et le détail de la quittance avant de valider.");
        var d = await Get(id);
        Verifier(d, StatutDeclaration.Soumise);
        // L'inspecteur choisit le détail : le taux (ou le tarif) est lu dans DetailQs.
        // La pénalité dépend de la date : elle est calculée au moment de la validation.
        // Évaluation du vérificateur : peut être exigée (Evaluation:Obligatoire = true) et fournir la base de calcul.
        var ev = await _db.Evaluations.AsNoTracking().FirstOrDefaultAsync(e => e.DeclarationId == id);
        if (_cfg.GetValue<bool>("Evaluation:Obligatoire") && ev == null)
            throw new InvalidOperationException("Le dossier doit d'abord être évalué par le vérificateur.");
        if (utiliserReference && ev == null)
            throw new InvalidOperationException("Aucune évaluation du vérificateur : impossible d'utiliser le montant de référence.");
        var baseMontant = utiliserReference ? ev!.MontantReference : d.PrixVente;
        var calc = await _droits.CalculerAsync(codeQ, baseMontant, d.DateActe);
        d.CodeQ = codeQ; d.MontantRetenu = baseMontant; d.Prix = calc.Prix; d.Penalite = calc.Penalite;
        d.OrdreVersement = new OrdreVersement
        {
            Numero = $"OV-{DateTime.UtcNow:yyyy}-{d.Id:D6}",
            Montant = calc.Total,
            DateGeneration = DateTime.UtcNow
        };
        Changer(d, StatutDeclaration.InstancePaiement, inspecteurId);
        await _db.SaveChangesAsync();
        await _notif.NotifierAsync(d.NotaireId,
            $"Déclaration {d.Reference} validée : prête au paiement (ordre de versement {d.OrdreVersement.Numero})", d.Id);
    }

    private static readonly Regex QuittanceOk = new(@"^[A-Za-z0-9\-/_.]{3,50}$");
    private static readonly Regex PdfOk = new(@"^[0-9a-fA-F-]{36}\.pdf$");
    private static string? ControlerPdf(string? nom)
    {
        if (string.IsNullOrWhiteSpace(nom)) return null;
        if (!PdfOk.IsMatch(nom)) throw new ArgumentException("Nom de fichier invalide : utilisez le fichier renvoyé par l'upload.");
        return nom;
    }
    private async Task<string> NumeroValideAsync(string? numero, int ignorerPaiementId = 0)
    {
        var n = (numero ?? "").Trim();
        if (!QuittanceOk.IsMatch(n))
            throw new ArgumentException("Numéro de quittance invalide (3 à 50 caractères : lettres, chiffres, - / _ .).");
        if (await _db.Paiements.AnyAsync(p => p.NumeroQuittance == n && p.Id != ignorerPaiementId))
            throw new InvalidOperationException("Ce numéro de quittance existe déjà.");
        return n;
    }

    public async Task<Paiement> ConfirmerPaiementAsync(int ordreId, string numeroQuittance, string? fichier, string receveurId)
    {
        var numero = await NumeroValideAsync(numeroQuittance);
        var pdf = ControlerPdf(fichier);
        var ov = await _db.OrdresVersement.Include(o => o.Declaration).ThenInclude(d => d.Historique)
            .FirstOrDefaultAsync(o => o.Id == ordreId) ?? throw new KeyNotFoundException("Ordre introuvable.");
        if (ov.EstPaye) throw new InvalidOperationException("Cet ordre de versement est déjà payé.");
        Verifier(ov.Declaration, StatutDeclaration.InstancePaiement);

        var p = new Paiement
        {
            OrdreVersementId = ov.Id,
            NumeroQuittance = numero,   // saisi par le receveur
            FichierQuittance = pdf,
            DatePaiement = DateTime.UtcNow,
            ReceveurId = receveurId
        };
        ov.EstPaye = true;
        _db.Paiements.Add(p);
        Changer(ov.Declaration, StatutDeclaration.Payee, receveurId);
        await _db.SaveChangesAsync();

        await _notif.NotifierRoleAsync(Roles.Inspecteur,
            $"L'ordre de versement {ov.Numero} est payé : l'acte {ov.Declaration.Reference} peut être enregistré", ov.Declaration.Id);
        await _notif.NotifierAsync(ov.Declaration.NotaireId,
            $"Paiement de {ov.Numero} validé (quittance {p.NumeroQuittance})", ov.Declaration.Id);
        return p;
    }

    /// <summary>Le receveur corrige le numéro de quittance de ses paiements, tant que l'acte n'est pas enregistré.</summary>
    public async Task<Paiement> ModifierQuittanceAsync(int ordreId, string numeroQuittance, string? fichier, string receveurId)
    {
        var pdf = ControlerPdf(fichier);
        var p = await _db.Paiements.Include(x => x.OrdreVersement).ThenInclude(o => o.Declaration)
            .FirstOrDefaultAsync(x => x.OrdreVersementId == ordreId) ?? throw new KeyNotFoundException("Paiement introuvable.");
        if (p.ReceveurId != receveurId) throw new UnauthorizedAccessException();
        if (p.OrdreVersement.Declaration.Statut != StatutDeclaration.Payee)
            throw new InvalidOperationException("Le numéro ne peut plus être modifié : l'acte est déjà enregistré.");
        p.NumeroQuittance = await NumeroValideAsync(numeroQuittance, p.Id);
        if (pdf != null) p.FichierQuittance = pdf;
        await _db.SaveChangesAsync();
        await _notif.NotifierAsync(p.OrdreVersement.Declaration.NotaireId,
            $"Le numéro de quittance de {p.OrdreVersement.Numero} est : {p.NumeroQuittance}", p.OrdreVersement.Declaration.Id);
        return p;
    }

    public async Task<Declaration> EnregistrerAsync(int id, string inspecteurId)
    {
        var d = await Get(id);
        Verifier(d, StatutDeclaration.Payee);

        var annee = DateTime.UtcNow.Year;
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var c = await _db.Compteurs.FirstOrDefaultAsync(x => x.Annee == annee);
        if (c == null) { c = new CompteurEnregistrement { Annee = annee }; _db.Compteurs.Add(c); }
        c.Valeur++;
        d.NumeroEnregistrement = $"{annee}/{c.Valeur:D6}";
        d.DateEnregistrement = DateTime.UtcNow;
        Changer(d, StatutDeclaration.Enregistree, inspecteurId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _notif.NotifierAsync(d.NotaireId, $"Acte {d.Reference} enregistré sous le n° {d.NumeroEnregistrement}", d.Id);
        // Le vérificateur doit contrôler l'acte enregistré et décider d'une éventuelle réévaluation.
        await _notif.NotifierRoleAsync(Roles.Verificateur,
            $"Acte {d.Reference} enregistré (n° {d.NumeroEnregistrement}) : contrôle de réévaluation requis", d.Id);
        return d;
    }

    private async Task<Declaration> Get(int id) =>
        await _db.Declarations.Include(x => x.OrdreVersement).Include(x => x.Historique)
            .FirstOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException("Déclaration introuvable.");

    private static void Verifier(Declaration d, params StatutDeclaration[] attendus)
    {
        if (!attendus.Contains(d.Statut))
            throw new InvalidOperationException($"Action impossible : la déclaration est au statut « {d.Statut} ».");
    }

    private static void Changer(Declaration d, StatutDeclaration s, string userId, string? com = null)
    {
        d.Statut = s;
        d.Historique.Add(new HistoriqueStatut { Statut = s, UserId = userId, Commentaire = com });
    }
}
