namespace Idara.API.Constants
{
    public static class UserRoles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string SchoolAdmin = "SchoolAdmin";
        public const string SchoolStaff = "SchoolStaff";
        public const string Teacher = "Teacher";
        public const string Guardian = "Guardian";
        public const string Surveillant = "Surveillant";

        /// <summary>
        /// Compte en LECTURE SEULE (observateur) : voit TOUT ce que voit le
        /// SchoolAdmin mais ne peut RIEN modifier. Le titulaire peut être le
        /// propriétaire de l'école, un superviseur du directeur, un auditeur, etc.
        /// — le rôle décrit l'ACCÈS (lecture seule), pas une fonction précise (la
        /// fonction libre est portée par User.JobTitle). La surface de lecture est
        /// obtenue via des claims de rôle secondaires (SchoolAdmin/SchoolStaff)
        /// dans le JWT ; toute écriture est bloquée par ReadOnlyRoleMiddleware
        /// (via le claim dédié "readonly"). Ne JAMAIS ajouter ce rôle à un
        /// `[Authorize(Roles=...)]` d'écriture (le middleware est le garde-fou).
        /// </summary>
        public const string SchoolViewer = "SchoolViewer";
    }
}
