using Notariat.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Notariat.Api.Services;

public interface IPdfService
{
    byte[] GenererOrdreVersement(Declaration d);
    byte[] GenererQuittance(Paiement p);
}

public class PdfService : IPdfService
{
    private static void Ligne(QuestPDF.Fluent.ColumnDescriptor col, string label, string valeur) =>
        col.Item().Row(r =>
        {
            r.ConstantItem(150).Text(label).SemiBold();
            r.RelativeItem().Text(valeur);
        });

    public byte[] GenererOrdreVersement(Declaration d) =>
        Document.Create(c => c.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(40); p.DefaultTextStyle(t => t.FontSize(11));
            p.Header().Column(h =>
            {
                h.Item().Text("République Algérienne Démocratique et Populaire").FontSize(9);
                h.Item().Text("ORDRE DE VERSEMENT").FontSize(22).Bold();
            });
            p.Content().PaddingVertical(20).Column(col =>
            {
                col.Spacing(8);
                Ligne(col, "N° ordre", d.OrdreVersement!.Numero);
                Ligne(col, "Date", d.OrdreVersement.DateGeneration.ToString("dd/MM/yyyy"));
                Ligne(col, "Notaire", d.Notaire.NomComplet);
                Ligne(col, "Référence de l'acte", d.Reference);
                Ligne(col, "Type d'acte", d.TypeActe);
                Ligne(col, "Vendeur", d.VendeurNom);
                Ligne(col, "Acheteur", d.AcheteurNom);
                Ligne(col, "Bien", d.DesignationBien);
                Ligne(col, "Montant de l'acte", $"{d.PrixVente:N2} DA");
                Ligne(col, "Droits", $"{d.Prix:N2} DA");
                Ligne(col, "Pénalité de retard", $"{d.Penalite:N2} DA");
                col.Item().PaddingTop(15).Text($"Montant à verser : {d.OrdreVersement.Montant:N2} DA").FontSize(15).Bold();
                col.Item().PaddingTop(10).Text("À présenter au guichet du Receveur pour paiement.").Italic();
            });
            p.Footer().AlignCenter().Text(t => { t.Span("Page "); t.CurrentPageNumber(); });
        })).GeneratePdf();

    public byte[] GenererQuittance(Paiement p) =>
        Document.Create(c => c.Page(pg =>
        {
            pg.Size(PageSizes.A4); pg.Margin(40); pg.DefaultTextStyle(t => t.FontSize(11));
            pg.Header().Text("QUITTANCE DE PAIEMENT").FontSize(22).Bold();
            pg.Content().PaddingVertical(20).Column(col =>
            {
                col.Spacing(8);
                Ligne(col, "N° quittance", p.NumeroQuittance);
                Ligne(col, "Ordre de versement", p.OrdreVersement.Numero);
                Ligne(col, "Acte", p.OrdreVersement.Declaration.Reference);
                Ligne(col, "Date de paiement", p.DatePaiement.ToString("dd/MM/yyyy HH:mm"));
                col.Item().PaddingTop(15).Text($"Montant payé : {p.OrdreVersement.Montant:N2} DA").FontSize(15).Bold();
            });
        })).GeneratePdf();
}
