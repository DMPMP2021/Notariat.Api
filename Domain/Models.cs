//using System.Text.Json.Serialization;
//using Microsoft.AspNetCore.Identity;

//namespace Notariat.Api.Domain;

//public static class Roles
//{
//    public const string Notaire = "Notaire";
//    public const string Inspecteur = "Inspecteur";
//    public const string Receveur = "Receveur";
//    public static readonly string[] All = { Notaire, Inspecteur, Receveur };
//}

//public enum StatutDeclaration { Brouillon, Soumise, Rejetee, InstancePaiement, Payee, Enregistree }

//public class ApplicationUser : IdentityUser
//{
//    public string NomComplet { get; set; } = default!;

//    // Champs sensibles : jamais renvoyés au navigateur quand une déclaration inclut son notaire.
//    [JsonIgnore] public override string? PasswordHash { get => base.PasswordHash; set => base.PasswordHash = value; }
//    [JsonIgnore] public override string? SecurityStamp { get => base.SecurityStamp; set => base.SecurityStamp = value; }
//    [JsonIgnore] public override string? ConcurrencyStamp { get => base.ConcurrencyStamp; set => base.ConcurrencyStamp = value; }
//    [JsonIgnore] public override string? NormalizedUserName { get => base.NormalizedUserName; set => base.NormalizedUserName = value; }
//    [JsonIgnore] public override string? NormalizedEmail { get => base.NormalizedEmail; set => base.NormalizedEmail = value; }
//    [JsonIgnore] public override string? Email { get => base.Email; set => base.Email = value; }
//    [JsonIgnore] public override string? PhoneNumber { get => base.PhoneNumber; set => base.PhoneNumber = value; }
//}

//public class Declaration
//{
//    public int Id { get; set; }
//    public string Reference { get; set; } = default!;
//    public string NotaireId { get; set; } = default!;
//    public ApplicationUser Notaire { get; set; } = default!;
//    public string TypeActe { get; set; } = "Vente";
//    public string VendeurNom { get; set; } = default!;
//    public string AcheteurNom { get; set; } = default!;
//    public string DesignationBien { get; set; } = default!;
//    public decimal PrixVente { get; set; }
//    public DateTime DateActe { get; set; }
//    public string? CodeQ { get; set; }               // code de quittance (table DetailQs)
//    public decimal Prix { get; set; }                // droits calculés (tarif ou taux)
//    public decimal Penalite { get; set; }            // pénalité de retard
//    public string? CheminFichier { get; set; }       // nom du fichier de l'acte (uploads/actes)
//    public StatutDeclaration Statut { get; set; } = StatutDeclaration.Brouillon;
//    public string? MotifRejet { get; set; }
//    public string? NumeroEnregistrement { get; set; }
//    public DateTime? DateEnregistrement { get; set; }
//    public OrdreVersement? OrdreVersement { get; set; }
//    public List<HistoriqueStatut> Historique { get; set; } = new();
//}

//public class OrdreVersement
//{
//    public int Id { get; set; }
//    public string Numero { get; set; } = default!;
//    public int DeclarationId { get; set; }
//    public Declaration Declaration { get; set; } = default!;
//    public decimal Montant { get; set; }
//    public DateTime DateGeneration { get; set; }
//    public bool EstPaye { get; set; }
//    public Paiement? Paiement { get; set; }
//}

//public class Paiement
//{
//    public int Id { get; set; }
//    public int OrdreVersementId { get; set; }
//    public OrdreVersement OrdreVersement { get; set; } = default!;
//    public string NumeroQuittance { get; set; } = default!;
//    public string? FichierQuittance { get; set; }   // PDF de la quittance joint par le receveur (uploads/quittances)
//    public DateTime DatePaiement { get; set; }
//    public string ReceveurId { get; set; } = default!;
//}

//public class Notification
//{
//    public int Id { get; set; }
//    public string UserId { get; set; } = default!;
//    public string Message { get; set; } = default!;
//    public int? DeclarationId { get; set; }
//    public bool Lue { get; set; }
//    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
//}

//public class HistoriqueStatut
//{
//    public int Id { get; set; }
//    public int DeclarationId { get; set; }
//    public StatutDeclaration Statut { get; set; }
//    public string UserId { get; set; } = default!;
//    public DateTime Date { get; set; } = DateTime.UtcNow;
//    public string? Commentaire { get; set; }
//}

//public class CompteurEnregistrement
//{
//    public int Annee { get; set; }
//    public int Valeur { get; set; }
//}

//public record LoginDto(string UserName, string Password);
//public record DeclarationDto(string VendeurNom, string AcheteurNom, string DesignationBien, decimal PrixVente, DateTime DateActe, string? CheminFichier);
//public record ValiderDto(string CodeQ);
//public record PaiementDto(string NumeroQuittance, string? FichierQuittance = null);
//public record CalculDroits(decimal Prix, decimal Penalite, decimal Total, int MoisRetard, decimal PenalitePct);
//public record RejetDto(string Motif);

//// ---- Tables de référence (mêmes colonnes que le projet QUITTANCEBE)
//public class CategorieQ
//{
//    [System.ComponentModel.DataAnnotations.Key]
//    public string? CodeCategQ { get; set; }
//    public string? LibelleCategQFr { get; set; }
//    public string? LibelleCategQAr { get; set; }
//}

//[System.ComponentModel.DataAnnotations.Schema.Table("DetailQs")]
//public class DetailQ
//{
//    [System.ComponentModel.DataAnnotations.Key]
//    public string? CodeQ { get; set; }
//    public string? CodeCategQ { get; set; }
//    public string? LibelleQFr { get; set; }
//    public string? LibelleQAr { get; set; }
//    public int Tarif { get; set; }
//    public decimal Taux { get; set; }
//}
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Notariat.Api.Domain;

public static class Roles
{
    public const string Notaire = "Notaire";
    public const string Inspecteur = "Inspecteur";
    public const string Receveur = "Receveur";
    public const string Verificateur = "Verificateur";
    public static readonly string[] All = { Notaire, Inspecteur, Receveur, Verificateur };
}

public enum StatutDeclaration { Brouillon, Soumise, Rejetee, InstancePaiement, Payee, Enregistree }
public enum ConclusionEvaluation { Conforme, SousEvalue, SurEvalue }

public class ApplicationUser : IdentityUser
{
    public string NomComplet { get; set; } = default!;

    // Champs sensibles : jamais renvoyés au navigateur quand une déclaration inclut son notaire.
    [JsonIgnore] public override string? PasswordHash { get => base.PasswordHash; set => base.PasswordHash = value; }
    [JsonIgnore] public override string? SecurityStamp { get => base.SecurityStamp; set => base.SecurityStamp = value; }
    [JsonIgnore] public override string? ConcurrencyStamp { get => base.ConcurrencyStamp; set => base.ConcurrencyStamp = value; }
    [JsonIgnore] public override string? NormalizedUserName { get => base.NormalizedUserName; set => base.NormalizedUserName = value; }
    [JsonIgnore] public override string? NormalizedEmail { get => base.NormalizedEmail; set => base.NormalizedEmail = value; }
    [JsonIgnore] public override string? Email { get => base.Email; set => base.Email = value; }
    [JsonIgnore] public override string? PhoneNumber { get => base.PhoneNumber; set => base.PhoneNumber = value; }
}

public class Declaration
{
    public int Id { get; set; }
    public string Reference { get; set; } = default!;
    public string NotaireId { get; set; } = default!;
    public ApplicationUser Notaire { get; set; } = default!;
    public string TypeActe { get; set; } = "Vente";
    public string VendeurNom { get; set; } = default!;
    public string AcheteurNom { get; set; } = default!;
    public string DesignationBien { get; set; } = default!;
    public decimal PrixVente { get; set; }
    public DateTime DateActe { get; set; }
    public string? CodeQ { get; set; }               // code de quittance (table DetailQs)
    public decimal Prix { get; set; }                // droits calculés (tarif ou taux)
    public decimal Penalite { get; set; }            // pénalité de retard
    public decimal MontantRetenu { get; set; }       // base de calcul utilisée par l'inspecteur (déclaré ou référence)
    public Evaluation? Evaluation { get; set; }      // évaluation du vérificateur
    public string? CheminFichier { get; set; }       // nom du fichier de l'acte (uploads/actes)
    public StatutDeclaration Statut { get; set; } = StatutDeclaration.Brouillon;
    public string? MotifRejet { get; set; }
    public string? NumeroEnregistrement { get; set; }
    public DateTime? DateEnregistrement { get; set; }
    public OrdreVersement? OrdreVersement { get; set; }
    public List<HistoriqueStatut> Historique { get; set; } = new();
}

public class OrdreVersement
{
    public int Id { get; set; }
    public string Numero { get; set; } = default!;
    public int DeclarationId { get; set; }
    public Declaration Declaration { get; set; } = default!;
    public decimal Montant { get; set; }
    public DateTime DateGeneration { get; set; }
    public bool EstPaye { get; set; }
    public Paiement? Paiement { get; set; }
}

public class Paiement
{
    public int Id { get; set; }
    public int OrdreVersementId { get; set; }
    public OrdreVersement OrdreVersement { get; set; } = default!;
    public string NumeroQuittance { get; set; } = default!;
    public string? FichierQuittance { get; set; }   // PDF de la quittance joint par le receveur (uploads/quittances)
    public DateTime DatePaiement { get; set; }
    public string ReceveurId { get; set; } = default!;
}

public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public string Message { get; set; } = default!;
    public int? DeclarationId { get; set; }
    public bool Lue { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
}

public class HistoriqueStatut
{
    public int Id { get; set; }
    public int DeclarationId { get; set; }
    public StatutDeclaration Statut { get; set; }
    public string UserId { get; set; } = default!;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string? Commentaire { get; set; }
}

public class CompteurEnregistrement
{
    public int Annee { get; set; }
    public int Valeur { get; set; }
}

public record LoginDto(string UserName, string Password);
public record DeclarationDto(string VendeurNom, string AcheteurNom, string DesignationBien, decimal PrixVente, DateTime DateActe, string? CheminFichier);
public record ValiderDto(string CodeQ, bool UtiliserReference = false);
public record EvaluationDto(decimal MontantReference, string? Observation);
public record PaiementDto(string NumeroQuittance, string? FichierQuittance = null);
public record CalculDroits(decimal Prix, decimal Penalite, decimal Total, int MoisRetard, decimal PenalitePct);
public record RejetDto(string Motif);

// ---- Tables de référence (mêmes colonnes que le projet QUITTANCEBE)
public class CategorieQ
{
    [System.ComponentModel.DataAnnotations.Key]
    public string? CodeCategQ { get; set; }
    public string? LibelleCategQFr { get; set; }
    public string? LibelleCategQAr { get; set; }
}

[System.ComponentModel.DataAnnotations.Schema.Table("DetailQs")]
public class DetailQ
{
    [System.ComponentModel.DataAnnotations.Key]
    public string? CodeQ { get; set; }
    public string? CodeCategQ { get; set; }
    public string? LibelleQFr { get; set; }
    public string? LibelleQAr { get; set; }
    public int Tarif { get; set; }
    public decimal Taux { get; set; }
}

/// <summary>Réévaluation d'un acte par le vérificateur : montant de référence comparé au montant déclaré.</summary>
public class Evaluation
{
    public int Id { get; set; }
    public int DeclarationId { get; set; }
    public Declaration Declaration { get; set; } = default!;
    public string VerificateurId { get; set; } = default!;
    public string? VerificateurNom { get; set; }
    public decimal MontantReference { get; set; }   // saisi par le vérificateur
    public decimal MontantDeclare { get; set; }     // montant déclaré par le notaire au moment de l'évaluation
    public decimal Ecart { get; set; }              // déclaré - référence (négatif = sous-évalué)
    public decimal EcartPourcent { get; set; }
    public ConclusionEvaluation Conclusion { get; set; }
    public string? Observation { get; set; }
    public DateTime DateEvaluation { get; set; } = DateTime.UtcNow;
}
