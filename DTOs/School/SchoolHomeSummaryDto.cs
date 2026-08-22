using Idara.API.Enums;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Une étape du parcours de démarrage, telle que le serveur la voit.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Tout est décidé ici, rien côté application</b> : l'ordre, le
    /// « c'est fait », le « c'est bloquant », le « c'est écarté ». Recalculer
    /// l'un de ces quatre côté client ferait diverger l'écran d'accueil d'un
    /// futur export ou d'un futur rappel — c'est la discipline déjà tenue pour
    /// l'avancement d'un objectif (§144) et le verrou 48 h (§151).
    /// </remarks>
    public class SchoolSetupStepDto
    {
        public SchoolSetupStep Step { get; set; }

        /// <summary>Le réglage est en place.</summary>
        public bool Done { get; set; }

        /// <summary>L'école a répondu « pas pour mon daara ».</summary>
        public bool Dismissed { get; set; }

        /// <summary>
        /// Sans ce réglage, l'application n'a rien à montrer du quotidien —
        /// parler d'impayés à un daara sans élèves n'aurait aucun sens. Les
        /// étapes bloquantes occupent l'accueil ; les autres attendent sous les
        /// alertes du jour. Une étape bloquante ne peut pas être écartée.
        /// </summary>
        public bool Blocking { get; set; }
    }

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

        /// <summary>
        /// Le parcours de démarrage complet, dans l'ordre où les réglages se
        /// commandent les uns les autres.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Additif</b> : les quatre booléens ci-dessus restent exposés
        /// pour les versions de l'application antérieures au 2026-08-22, qui
        /// ignorent cette liste. Les retirer viderait leur carte de démarrage.
        /// </remarks>
        public List<SchoolSetupStepDto> SetupSteps { get; set; } = new();

        /// <summary>
        /// Enseignants du daara qui n'ont aucune classe affectée.
        /// </summary>
        /// <remarks>
        /// Ils ouvrent leur espace sur « aucune classe ne vous est affectée »
        /// (§150) : correct, et sans issue pour eux. C'est la <b>direction</b>
        /// qui peut agir, d'où la remontée ici et non chez l'enseignant.
        /// </remarks>
        public int TeachersWithoutClass { get; set; }
    }
}
