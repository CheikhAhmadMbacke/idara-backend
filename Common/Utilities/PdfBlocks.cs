using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Fragments de mise en page partagés par TOUS les documents PDF d'Idara
    /// (reçus de paiement, de don, de virement). Source unique : un reçu de don
    /// et un reçu de virement doivent présenter leurs références de la même
    /// façon, sinon on relit deux documents différents pour la même information.
    /// </summary>
    public static class PdfBlocks
    {
        /// <summary>
        /// Bloc « Références » d'un reçu : la nôtre et celle du prestataire.
        ///
        /// <para>Il est en PLEINE LARGEUR, avec la valeur en chasse fixe. Ce n'est
        /// pas cosmétique : un identifiant de décaissement fait une quarantaine de
        /// caractères sans le moindre espace. Placé dans la colonne de droite d'un
        /// tableau à deux colonnes sur une page A5, il n'aurait jamais tenu. Ici il
        /// se replie proprement — QuestPDF coupe seul un bloc sans espace depuis
        /// 2024.3, <c>WrapAnywhere</c> étant obsolète (§116).</para>
        ///
        /// <para>La référence du prestataire n'est affichée que si elle existe : un
        /// virement tout juste initié n'en a pas encore, et imprimer « — » à sa
        /// place laisserait croire à une anomalie.</para>
        /// </summary>
        public static void References(
            IContainer container, string reference, string? providerReference,
            string labelColor, string borderColor)
        {
            container.Border(1).BorderColor(borderColor).Padding(9).Column(col =>
            {
                col.Item().PaddingBottom(4).Text("RÉFÉRENCES")
                    .FontSize(7.5f).SemiBold().FontColor(labelColor);

                Line(col, "Idara", reference);
                if (!string.IsNullOrWhiteSpace(providerReference))
                    Line(col, "SenePay", providerReference!);
            });

            void Line(ColumnDescriptor col, string label, string value)
            {
                col.Item().PaddingTop(2).Row(row =>
                {
                    row.ConstantItem(58).Text(label).FontSize(8.5f).FontColor(labelColor);
                    row.RelativeItem().Text(value).FontSize(8).FontFamily(Fonts.Consolas);
                });
            }
        }
    }
}
