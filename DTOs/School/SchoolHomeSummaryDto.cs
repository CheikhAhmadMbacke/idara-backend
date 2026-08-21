namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Ce que la direction d'un daara vient réellement chercher en ouvrant
    /// l'application : <b>qui n'a pas payé</b>, <b>si la présence du jour est
    /// faite</b>, <b>ce qu'il y a en caisse</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// L'ancien tableau de bord affichait quatre compteurs — élèves,
    /// enseignants, responsables, classes — qui n'appelaient aucune action. La
    /// direction ne se connecte pas pour savoir combien elle a d'élèves : elle
    /// le sait. Ces trois réponses-là, en revanche, demandaient deux à trois
    /// écrans et jusqu'à 3,3 écrans de défilement.
    /// </para>
    /// <para>
    /// <b>Un seul appel, et pas trois.</b> Sur un forfait sénégalais, trois
    /// allers-retours coûtent plus d'une seconde ; et trois réponses arrivées
    /// séparément peuvent se contredire à l'affichage.
    /// </para>
    /// <para>
    /// ⚠️ Cette réponse porte des <b>SOLDES</b> : elle ne doit jamais être mise
    /// en cache hors ligne côté application. De l'argent affiché « comme la
    /// dernière fois » est pire qu'un écran vide.
    /// </para>
    /// </remarks>
    public class SchoolHomeSummaryDto
    {
        /// <summary>Mois observé (1er du mois, UTC).</summary>
        public DateTime PeriodStart { get; set; }

        /// <summary>Élèves dont la mensualité du mois est en retard — échéance
        /// dépassée et facture non soldée.</summary>
        public int OverdueStudents { get; set; }

        /// <summary>Élèves dont la mensualité est émise mais pas encore
        /// échue. Sert à nuancer : « en attente » n'est pas « en retard ».</summary>
        public int PendingStudents { get; set; }

        /// <summary>Élèves inscrits ce mois-ci, tous statuts confondus.</summary>
        public int EnrolledStudents { get; set; }

        /// <summary>La présence des élèves a-t-elle été pointée aujourd'hui ?
        /// </summary>
        public bool AttendanceTakenToday { get; set; }

        /// <summary>Nombre d'élèves pointés aujourd'hui, pour dire « 42 sur
        /// 87 » quand le pointage est commencé sans être fini.</summary>
        public int AttendanceCountToday { get; set; }

        /// <summary>Espèces en caisse, toutes périodes confondues.</summary>
        public long CashBalanceFcfa { get; set; }

        /// <summary>Ce qui est disponible sur le compte en ligne du daara.
        /// </summary>
        public long OnlineBalanceFcfa { get; set; }

        /// <summary>Le daara a-t-il au moins une classe ? Un daara qui vient
        /// d'être validé n'a rien, et c'est le pire moment du parcours : il
        /// faut lui dire par où commencer plutôt que de lui montrer des
        /// zéros.</summary>
        public bool HasClasses { get; set; }

        /// <summary>Le daara a-t-il au moins un élève ?</summary>
        public bool HasStudents { get; set; }

        /// <summary>Un tarif est-il configuré (général, par classe ou par
        /// régime) ? Sans lui, aucune facture ne sera jamais générée.</summary>
        public bool HasFees { get; set; }

        /// <summary>Le daara a-t-il invité au moins un compte (enseignant,
        /// personnel, parent) ?</summary>
        public bool HasInvitedUsers { get; set; }
    }
}
