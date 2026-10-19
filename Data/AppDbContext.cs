using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Notariat.Api.Domain;

namespace Notariat.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> o) : base(o) { }
    public DbSet<Declaration> Declarations => Set<Declaration>();
    public DbSet<OrdreVersement> OrdresVersement => Set<OrdreVersement>();
    public DbSet<Paiement> Paiements => Set<Paiement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<HistoriqueStatut> Historiques => Set<HistoriqueStatut>();
    public DbSet<CompteurEnregistrement> Compteurs => Set<CompteurEnregistrement>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<CategorieQ> CategorieQs => Set<CategorieQ>();
    public DbSet<DetailQ> DetailQs => Set<DetailQ>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Declaration>().HasIndex(d => d.Reference).IsUnique();
        b.Entity<Declaration>().HasIndex(d => d.NumeroEnregistrement).IsUnique().HasFilter("[NumeroEnregistrement] IS NOT NULL");
        b.Entity<Declaration>().Property(d => d.PrixVente).HasPrecision(18, 2);
        b.Entity<Declaration>().Property(d => d.Prix).HasPrecision(18, 2);
        b.Entity<Declaration>().Property(d => d.Penalite).HasPrecision(18, 2);
        b.Entity<DetailQ>().Property(d => d.Taux).HasPrecision(18, 4);
        b.Entity<OrdreVersement>().Property(o => o.Montant).HasPrecision(18, 2);
        b.Entity<OrdreVersement>().HasOne(o => o.Declaration).WithOne(d => d.OrdreVersement).HasForeignKey<OrdreVersement>(o => o.DeclarationId);
        b.Entity<Paiement>().HasOne(p => p.OrdreVersement).WithOne(o => o.Paiement).HasForeignKey<Paiement>(p => p.OrdreVersementId);
        b.Entity<Declaration>().Property(d => d.MontantRetenu).HasPrecision(18, 2);
        b.Entity<Evaluation>().Property(e => e.MontantReference).HasPrecision(18, 2);
        b.Entity<Evaluation>().Property(e => e.MontantDeclare).HasPrecision(18, 2);
        b.Entity<Evaluation>().Property(e => e.Ecart).HasPrecision(18, 2);
        b.Entity<Evaluation>().Property(e => e.EcartPourcent).HasPrecision(18, 2);
        b.Entity<Evaluation>().HasOne(e => e.Declaration).WithOne(d => d.Evaluation).HasForeignKey<Evaluation>(e => e.DeclarationId);
        b.Entity<Evaluation>().HasIndex(e => e.DeclarationId).IsUnique();
        b.Entity<Paiement>().Property(p => p.NumeroQuittance).HasMaxLength(50);
        b.Entity<Paiement>().HasIndex(p => p.NumeroQuittance).IsUnique();
        b.Entity<CompteurEnregistrement>().HasKey(c => c.Annee);
        b.Entity<CompteurEnregistrement>().Property(c => c.Annee).ValueGeneratedNever();
    }
}

public static class Seeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var rm = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var um = sp.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var r in Roles.All)
            if (!await rm.RoleExistsAsync(r)) await rm.CreateAsync(new IdentityRole(r));

        async Task User(string name, string full, string role)
        {
            if (await um.FindByNameAsync(name) != null) return;
            var u = new ApplicationUser { UserName = name, Email = $"{name}@test.dz", NomComplet = full, EmailConfirmed = true };
            await um.CreateAsync(u, "Test#2026");
            await um.AddToRoleAsync(u, role);
        }
        await User("notaire1", "Me Karim Benali", Roles.Notaire);
        await User("notaire2", "Me Samia Haddad", Roles.Notaire);
        await User("inspecteur1", "M. Yacine Messaoud", Roles.Inspecteur);
        await User("receveur1", "Mme Nadia Cherif", Roles.Receveur);
        await User("verificateur1", "M. Amine Boudiaf", Roles.Verificateur);
        await User("verificateur2", "Mme Lila Ferhat", Roles.Verificateur);
        await User("verificateur3", "M. Sofiane Rahmani", Roles.Verificateur);
    }
}
