using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;
using Notariat.Api.Domain;

namespace Notariat.Api.Services;

public interface IDroitsService
{
    Task<CalculDroits> CalculerAsync(string codeQ, decimal montant, DateTime dateSign);
}

/// <summary>Calcul des droits : tarif fixe ou taux (% du montant), puis pénalité de retard selon la date de signature.</summary>
public class DroitsService : IDroitsService
{
    private readonly AppDbContext _db;
    public DroitsService(AppDbContext db) => _db = db;

    public async Task<CalculDroits> CalculerAsync(string codeQ, decimal montant, DateTime dateSign)
    {
        var detailQ = await _db.DetailQs.AsNoTracking()
            .Where(t => t.CodeQ == codeQ)
            .Select(t => new { t.Taux, t.Tarif })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Le code de quittance spécifié n'existe pas.");

        decimal prixFinal = detailQ.Tarif;
        if (detailQ.Taux > 0 && montant > 0)
            prixFinal = (montant * detailQ.Taux) / 100;

        var today = DateTime.UtcNow.Date;
        var d = dateSign.Date;
        int diffMonths = ((today.Year - d.Year) * 12) + today.Month - d.Month;
        if (today.Day < d.Day) diffMonths--;

        decimal pct = diffMonths switch
        {
            1 => 10, 2 => 13, 3 => 16, 4 => 19, 5 => 22,
            >= 6 => 25,
            _ => 0
        };
        var penalite = Math.Round((prixFinal * pct) / 100, 2);
        prixFinal = Math.Round(prixFinal, 2);
        return new CalculDroits(prixFinal, penalite, prixFinal + penalite, Math.Max(diffMonths, 0), pct);
    }
}
