namespace Idara.API.Models
{
    /// <summary>
    /// La facture Sonatel telle qu'elle a été REÇUE, saisie à la main, pour être
    /// confrontée au total qu'Idara a calculé de son côté.
    ///
    /// <para><b>Pourquoi la saisir plutôt que la déduire.</b> Tout l'objet du
    /// registre est de ne plus subir de surprise en fin de mois. Or un total
    /// calculé, si juste soit-il, ne prouve rien tant qu'il n'a pas été mis en
    /// face du document qui, lui, engage un paiement. C'est l'ÉCART entre les
    /// deux qui est l'information : nul, il valide le modèle de coût ; positif,
    /// il dit soit qu'on s'est trompé d'hypothèse de facturation, soit que des
    /// SMS sont partis en dehors d'Idara — c'est-à-dire exactement le scénario
    /// qu'on cherche à ne pas découvrir trop tard.</para>
    ///
    /// <para>La facture Orange porte aussi une <b>redevance mensuelle</b> (10 000 F
    /// HT même à zéro SMS) et la TVA : sans les compter, l'écart paraîtrait
    /// toujours anormal alors qu'il ne le serait pas.</para>
    /// </summary>
    public class SmsProviderInvoice
    {
        public int Id { get; set; }

        /// <summary>Mois facturé (unique avec <see cref="Year"/>).</summary>
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>Montant hors taxes lu sur la facture, en FCFA.</summary>
        public long AmountHtFcfa { get; set; }

        /// <summary>Montant toutes taxes comprises lu sur la facture, en FCFA.
        /// Zéro si la facture ne le détaille pas.</summary>
        public long AmountTtcFcfa { get; set; }

        /// <summary>
        /// Nombre de SMS ou de segments facturés, tel qu'annoncé par Orange, si
        /// la facture le détaille. C'est CE chiffre qui tranchera l'ambiguïté du
        /// contrat (« facturation par lot de 160 caractères ») en le comparant
        /// aux deux hypothèses calculées par Idara.
        /// </summary>
        public int? ProviderQuantity { get; set; }

        /// <summary>Note libre : référence de facture, remarque, litige en cours.</summary>
        public string? Note { get; set; }

        /// <summary>Compte SuperAdmin qui a saisi la facture (audit).</summary>
        public int RecordedById { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
