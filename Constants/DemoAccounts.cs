namespace Idara.API.Constants
{
    /// <summary>
    /// Identifiants des comptes de DÉMONSTRATION seedés par
    /// <see cref="Data.DbInitializer"/> (« École de démonstration »). Source
    /// UNIQUE — doit rester ALIGNÉE sur la Play Console (« Accès à
    /// l'application », utilisée par le reviewer Google). Exposés au SuperAdmin
    /// via le back-office (endpoint <c>/api/admin/demo/credentials</c>) pour
    /// être retrouvés à tout moment.
    /// </summary>
    public static class DemoAccounts
    {
        public const string SchoolName = "École de démonstration";
        public const string SchoolAdminEmail = "demo.ecole@idara.sn";
        public const string SchoolAdminPassword = "DemoIdara2026!";
        public const string GuardianPhone = "+221770000000";
        public const string GuardianCode = "123456";

        /// <summary>
        /// Enseignant de démo — identité par téléphone, comme tout compte créé
        /// par une école (§91). Numéro voisin de celui du parent pour rester
        /// mémorisable, même code à 6 chiffres.
        /// </summary>
        public const string TeacherPhone = "+221770000001";
        public const string TeacherCode = "123456";
    }
}
