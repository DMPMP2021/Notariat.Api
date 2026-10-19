using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;
using Notariat.Api.Domain;
using Notariat.Api.Services;

namespace Notariat.Api.Controllers;

[ApiController, Route("api/declarations"), Authorize]
public class DeclarationsController : ControllerBase
{
    private readonly IDeclarationService _svc;
    private readonly AppDbContext _db;
    private readonly IPdfService _pdf;
    private readonly IDroitsService _droits;
    private readonly IEvaluationService _eval;
    private readonly IWebHostEnvironment _env;
    public DeclarationsController(IDeclarationService svc, AppDbContext db, IPdfService pdf, IDroitsService droits, IEvaluationService eval, IWebHostEnvironment env)
    { _svc = svc; _db = db; _pdf = pdf; _droits = droits; _eval = eval; _env = env; }

    private string DossierQuittances => Path.Combine(_env.ContentRootPath, "uploads", "quittances");
    private string DossierActes => Path.Combine(_env.ContentRootPath, "uploads", "actes");

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private IQueryable<Declaration> Query() =>
        _db.Declarations.AsNoTracking().Include(d => d.Notaire).Include(d => d.OrdreVersement).ThenInclude(o => o.Paiement)
            .Include(d => d.Evaluation);

    // ---------- NOTAIRE ----------
    [HttpPost, Authorize(Roles = Roles.Notaire)]
    public async Task<IActionResult> Creer(DeclarationDto dto) => Ok(await _svc.CreerAsync(dto, UserId));

    [HttpPut("{id}"), Authorize(Roles = Roles.Notaire)]
    public async Task<IActionResult> Modifier(int id, DeclarationDto dto) => Ok(await _svc.ModifierAsync(id, dto, UserId));

    [HttpPost("{id}/soumettre"), Authorize(Roles = Roles.Notaire)]
    public async Task<IActionResult> Soumettre(int id) { await _svc.SoumettreAsync(id, UserId); return NoContent(); }

    [HttpGet("mes-declarations"), Authorize(Roles = Roles.Notaire)]
    public async Task<IActionResult> Mes([FromQuery] StatutDeclaration? statut)
    {
        var q = Query().Where(d => d.NotaireId == UserId);
        if (statut != null) q = q.Where(d => d.Statut == statut);
        return Ok(await q.OrderByDescending(d => d.Id).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(int id)
    {
        var d = await Query().Include(x => x.Historique).FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();
        if (User.IsInRole(Roles.Notaire) && d.NotaireId != UserId) return Forbid();
        return Ok(d);
    }

    [HttpGet("{id}/ordre-versement/pdf")]
    public async Task<IActionResult> OrdrePdf(int id)
    {
        var d = await Query().FirstOrDefaultAsync(x => x.Id == id);
        if (d?.OrdreVersement == null) return NotFound();
        if (User.IsInRole(Roles.Notaire) && d.NotaireId != UserId) return Forbid();
        return File(_pdf.GenererOrdreVersement(d), "application/pdf", $"{d.OrdreVersement.Numero}.pdf");
    }

    /// <summary>Aperçu des droits et de la pénalité avant l'envoi.</summary>
    [HttpGet("calcul"), Authorize(Roles = Roles.Inspecteur)]
    public async Task<IActionResult> Calcul([FromQuery] string codeQ, [FromQuery] decimal montant, [FromQuery] DateTime dateSign)
        => Ok(await _droits.CalculerAsync(codeQ, montant, dateSign));

    /// <summary>Upload de l'acte (PDF, DOC, DOCX). Renvoie le nom du fichier à mettre dans CheminFichier.</summary>
    [HttpPost("upload"), Authorize(Roles = Roles.Notaire), RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Aucun fichier envoyé." });
        var extension = Path.GetExtension(file.FileName).ToLower();
        if (!new[] { ".pdf", ".doc", ".docx" }.Contains(extension))
            return BadRequest(new { message = "Format de fichier non accepté. Seuls PDF, DOC et DOCX sont autorisés." });

        Directory.CreateDirectory(DossierActes);
        var fileName = $"{Guid.NewGuid()}{extension}";
        await using (var stream = new FileStream(Path.Combine(DossierActes, fileName), FileMode.Create))
            await file.CopyToAsync(stream);
        return Ok(new { fileName });
    }

    [HttpGet("{id}/fichier")]
    public async Task<IActionResult> Fichier(int id)
    {
        var d = await Query().FirstOrDefaultAsync(x => x.Id == id);
        if (d?.CheminFichier == null) return NotFound();
        if (User.IsInRole(Roles.Notaire) && d.NotaireId != UserId) return Forbid();
        var path = Path.Combine(DossierActes, Path.GetFileName(d.CheminFichier));
        if (!System.IO.File.Exists(path)) return NotFound();
        var ct = Path.GetExtension(path).ToLower() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            _ => "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };
        return PhysicalFile(path, ct, $"{d.Reference}{Path.GetExtension(path)}");
    }

    // ---------- VÉRIFICATEUR (c'est lui seul qui évalue) ----------
    /// <summary>Dossiers soumis, en attente de la décision de l'inspecteur (évalués ou non).</summary>
    [HttpGet("a-evaluer"), Authorize(Roles = Roles.Verificateur)]
    public async Task<IActionResult> AEvaluer() =>
        Ok(await Query().Where(d => d.Statut == StatutDeclaration.Soumise).OrderBy(d => d.Id).ToListAsync());

    /// <summary>Le vérificateur saisit le montant de référence ; le système le compare au montant déclaré.</summary>
    [HttpPost("{id}/evaluer"), Authorize(Roles = Roles.Verificateur)]
    public async Task<IActionResult> Evaluer(int id, EvaluationDto dto)
    {
        var ev = await _eval.EvaluerAsync(id, dto.MontantReference, dto.Observation, UserId, User.FindFirstValue(ClaimTypes.Name) ?? "");
        return Ok(ev);
    }

    /// <summary>Actes enregistrés en attente de contrôle de réévaluation par le vérificateur.
    /// Un acte est considéré « à réévaluer » s'il n'a pas encore été réévalué après son enregistrement
    /// (date d'évaluation antérieure à la date d'enregistrement, ou aucune évaluation).</summary>
    [HttpGet("a-reevaluer"), Authorize(Roles = Roles.Verificateur)]
    public async Task<IActionResult> AReevaluer()
    {
        var q = Query().Where(d => d.Statut == StatutDeclaration.Enregistree
            && (d.Evaluation == null || d.Evaluation.DateEvaluation < d.DateEnregistrement));
        return Ok(await q.OrderByDescending(d => d.DateEnregistrement).ToListAsync());
    }

    /// <summary>Le vérificateur réévalue un acte déjà enregistré : il saisit un nouveau montant de référence.</summary>
    [HttpPost("{id}/reevaluer"), Authorize(Roles = Roles.Verificateur)]
    public async Task<IActionResult> Reevaluer(int id, EvaluationDto dto)
    {
        var ev = await _eval.ReevaluerAsync(id, dto.MontantReference, dto.Observation, UserId, User.FindFirstValue(ClaimTypes.Name) ?? "");
        return Ok(ev);
    }

    /// <summary>Liste de tous les actes (lecture seule) : inspecteur, receveur et vérificateur. Les brouillons des notaires n'y figurent pas.</summary>
    [HttpGet("tous"), Authorize(Roles = "Inspecteur,Receveur,Verificateur")]
    public async Task<IActionResult> Tous([FromQuery] StatutDeclaration? statut)
    {
        var q = Query().Where(d => d.Statut != StatutDeclaration.Brouillon);
        if (statut != null) q = q.Where(d => d.Statut == statut);
        return Ok(await q.OrderByDescending(d => d.Id).Take(500).ToListAsync());
    }


    /// <summary>Écart toléré (%) : même valeur côté aperçu et côté serveur.</summary>
    [HttpGet("evaluation/tolerance")]
    public IActionResult Tolerance() => Ok(new { tolerancePourcent = _eval.Tolerance });

    // ---------- INSPECTEUR (il lit l'évaluation, il ne l'écrit pas) ----------
    [HttpGet("a-verifier"), Authorize(Roles = Roles.Inspecteur)]
    public async Task<IActionResult> AVerifier() =>
        Ok(await Query().Where(d => d.Statut == StatutDeclaration.Soumise).OrderBy(d => d.Id).ToListAsync());

    [HttpGet("a-enregistrer"), Authorize(Roles = Roles.Inspecteur)]
    public async Task<IActionResult> AEnregistrer() =>
        Ok(await Query().Where(d => d.Statut == StatutDeclaration.Payee).OrderBy(d => d.Id).ToListAsync());

    [HttpPost("{id}/valider"), Authorize(Roles = Roles.Inspecteur)]
    public async Task<IActionResult> Valider(int id, ValiderDto dto)
    { await _svc.ValiderAsync(id, dto.CodeQ, dto.UtiliserReference, UserId); return NoContent(); }

    [HttpPost("{id}/rejeter"), Authorize(Roles = Roles.Inspecteur)]
    public async Task<IActionResult> Rejeter(int id, RejetDto dto) { await _svc.RejeterAsync(id, dto.Motif, UserId); return NoContent(); }

    [HttpPost("{id}/enregistrer"), Authorize(Roles = Roles.Inspecteur)]
    public async Task<IActionResult> Enregistrer(int id)
    {
        var d = await _svc.EnregistrerAsync(id, UserId);
        return Ok(new { d.Id, d.NumeroEnregistrement });
    }

    // ---------- RECEVEUR ----------
    [HttpGet("ordres-a-payer"), Authorize(Roles = Roles.Receveur)]
    public async Task<IActionResult> OrdresAPayer() =>
        Ok(await _db.OrdresVersement.AsNoTracking().Include(o => o.Declaration).ThenInclude(d => d.Notaire)
            .Where(o => !o.EstPaye).OrderBy(o => o.Id).ToListAsync());

    [HttpPost("ordres/{ordreId}/payer"), Authorize(Roles = Roles.Receveur)]
    public async Task<IActionResult> Payer(int ordreId, PaiementDto dto)
    {
        var p = await _svc.ConfirmerPaiementAsync(ordreId, dto.NumeroQuittance, dto.FichierQuittance, UserId);
        return Ok(new { p.Id, p.NumeroQuittance, p.DatePaiement });
    }

    /// <summary>Le receveur envoie le PDF de la quittance (PDF uniquement, 10 Mo max).</summary>
    [HttpPost("quittance/upload"), Authorize(Roles = Roles.Receveur), RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadQuittance(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Aucun fichier envoyé." });
        if (Path.GetExtension(file.FileName).ToLower() != ".pdf")
            return BadRequest(new { message = "Seul le format PDF est accepté." });
        await using var input = file.OpenReadStream();
        var head = new byte[5];
        var n = await input.ReadAsync(head, 0, 5);
        if (n < 5 || System.Text.Encoding.ASCII.GetString(head) != "%PDF-")
            return BadRequest(new { message = "Le fichier n'est pas un PDF valide." });
        input.Position = 0;
        Directory.CreateDirectory(DossierQuittances);
        var fileName = $"{Guid.NewGuid()}.pdf";
        await using var output = new FileStream(Path.Combine(DossierQuittances, fileName), FileMode.Create);
        await input.CopyToAsync(output);
        return Ok(new { fileName });
    }

    /// <summary>PDF de la quittance joint par le receveur (notaire : uniquement ses actes).</summary>
    [HttpGet("ordres/{ordreId}/quittance/fichier"), Authorize(Roles = "Receveur,Notaire,Inspecteur")]
    public async Task<IActionResult> QuittanceFichier(int ordreId)
    {
        var p = await _db.Paiements.AsNoTracking().Include(x => x.OrdreVersement).ThenInclude(o => o.Declaration)
            .FirstOrDefaultAsync(x => x.OrdreVersementId == ordreId);
        if (p?.FichierQuittance == null) return NotFound();
        if (User.IsInRole(Roles.Notaire) && p.OrdreVersement.Declaration.NotaireId != UserId) return Forbid();
        var path = Path.Combine(DossierQuittances, Path.GetFileName(p.FichierQuittance));
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf", $"{p.NumeroQuittance}.pdf");
    }

    /// <summary>Le receveur met à jour le n° de quittance qu'il a saisi.</summary>
    [HttpPut("ordres/{ordreId}/quittance"), Authorize(Roles = Roles.Receveur)]
    public async Task<IActionResult> ModifierQuittance(int ordreId, PaiementDto dto)
    {
        var p = await _svc.ModifierQuittanceAsync(ordreId, dto.NumeroQuittance, dto.FichierQuittance, UserId);
        return Ok(new { p.Id, p.NumeroQuittance, p.FichierQuittance });
    }

    /// <summary>Quittances délivrées par le receveur connecté.</summary>
    [HttpGet("paiements-effectues"), Authorize(Roles = Roles.Receveur)]
    public async Task<IActionResult> PaiementsEffectues() =>
        Ok(await _db.Paiements.AsNoTracking()
            .Include(p => p.OrdreVersement).ThenInclude(o => o.Declaration)
            .Where(p => p.ReceveurId == UserId).OrderByDescending(p => p.Id).Take(100).ToListAsync());

    [HttpGet("ordres/{ordreId}/quittance/pdf"), Authorize(Roles = "Receveur,Notaire,Inspecteur")]
    public async Task<IActionResult> Quittance(int ordreId)
    {
        var p = await _db.Paiements.Include(x => x.OrdreVersement).ThenInclude(o => o.Declaration)
            .FirstOrDefaultAsync(x => x.OrdreVersementId == ordreId);
        if (p == null) return NotFound();
        if (User.IsInRole(Roles.Notaire) && p.OrdreVersement.Declaration.NotaireId != UserId) return Forbid();
        return File(_pdf.GenererQuittance(p), "application/pdf", $"{p.NumeroQuittance}.pdf");
    }
}
