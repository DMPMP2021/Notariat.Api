using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;
using Notariat.Api.Domain;

namespace Notariat.Api.Controllers;

[ApiController, Authorize]
[Route("api/[controller]")]
public class CategorieQController : ControllerBase
{
    private readonly AppDbContext _context;
    public CategorieQController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategorieQ>>> GetCategories()
        => await _context.CategorieQs.AsNoTracking().ToListAsync();
}

[ApiController, Authorize]
[Route("api/[controller]")]
public class DetailQController : ControllerBase
{
    private readonly AppDbContext _context;
    public DetailQController(AppDbContext context) => _context = context;

    /// <summary>Tous les détails (le formulaire filtre par catégorie côté client).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
        => Ok(await _context.DetailQs.AsNoTracking().Select(d => new
        {
            d.CodeQ, d.CodeCategQ, d.LibelleQFr, d.LibelleQAr, d.Taux, d.Tarif,
            Valeur = d.Taux > 0 ? d.Taux : d.Tarif
        }).ToListAsync());

    /// <summary>Détails d'une catégorie : retourne soit le taux soit le tarif.</summary>
    [HttpGet("{codeCategQ}")]
    public async Task<ActionResult<IEnumerable<object>>> GetDetailsByCategory(string codeCategQ)
    {
        if (string.IsNullOrWhiteSpace(codeCategQ)) return BadRequest("Le code de catégorie est requis.");
        var details = await _context.DetailQs.AsNoTracking()
            .Where(d => d.CodeCategQ == codeCategQ)
            .Select(d => new
            {
                d.CodeQ, d.CodeCategQ, d.LibelleQFr, d.LibelleQAr, d.Taux, d.Tarif,
                Valeur = d.Taux > 0 ? d.Taux : d.Tarif
            }).ToListAsync();
        if (!details.Any()) return NotFound($"Aucun détail trouvé pour la catégorie '{codeCategQ}'.");
        return Ok(details);
    }
}
