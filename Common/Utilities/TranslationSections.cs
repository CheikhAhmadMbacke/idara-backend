namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Noms FRANÇAIS des parties à relire.
    ///
    /// Les sections sont dérivées du préfixe technique de la clé
    /// (« school_info », « paylink », « tx »…). Ces mots ne veulent rien dire
    /// pour un relecteur bénévole : lui demander de choisir entre « cred » et
    /// « kyc » revient à lui demander de deviner. On lui montre donc « Mot de
    /// passe et identifiants » et « Inscription de l'école ».
    ///
    /// ⚠️ Le libellé est purement d'AFFICHAGE : la clé technique reste la
    /// section stockée et échangée avec le serveur. Renommer la section
    /// elle-même casserait le lien avec les clés de traduction.
    ///
    /// Une section inconnue (ajoutée plus tard) retombe sur son nom technique
    /// plutôt que de disparaître — mieux vaut un mot obscur qu'une partie
    /// invisible que personne ne relit jamais.
    /// </summary>
    public static class TranslationSections
    {
        private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
        {
            ["academic"] = "Année scolaire et périodes",
            ["account"] = "Mon compte",
            ["account_choice"] = "Choix du type de compte",
            ["assignments"] = "Affectation des enseignants",
            ["attendance"] = "Présences des élèves",
            ["auth"] = "Connexion",
            ["beneficiary"] = "Espace bénéficiaire (virements reçus)",
            ["branding"] = "Personnalisation de l'école",
            ["cash"] = "Caisse",
            ["changepw"] = "Changer le mot de passe",
            ["classes"] = "Classes",
            ["common"] = "Mots courants (Oui, Non, Annuler…)",
            ["conn"] = "État de la connexion",
            ["coran"] = "Suivi du Coran",
            ["cred"] = "Identifiants à communiquer",
            ["daara_events"] = "Vie de l'école (journal)",
            ["dashboard"] = "Écran d'accueil",
            ["debug"] = "Outils techniques",
            ["divers"] = "Divers",
            ["donation"] = "Dons",
            ["donor"] = "Espace donateur",
            ["donor_report"] = "Reçu de don",
            ["download"] = "Téléchargement de l'application",
            ["draft"] = "Saisie récupérée",
            ["errors"] = "Messages d'erreur",
            ["export"] = "Exports",
            ["exports"] = "Exports (suite)",
            ["finance"] = "Finances de l'école",
            ["forgot"] = "Mot de passe oublié",
            ["grades"] = "Notes",
            ["guardian"] = "Espace parent",
            ["home"] = "Accueil et alertes du jour",
            ["import"] = "Import en masse",
            ["incident"] = "Signalement d'un problème",
            ["invite"] = "Inviter un utilisateur",
            ["invoice"] = "Factures",
            ["journal"] = "Cahier de suivi",
            ["kyc"] = "Inscription de l'école",
            ["landing"] = "Page d'accueil du site",
            ["levels"] = "Niveaux de classe",
            ["logout"] = "Déconnexion",
            ["media_picker"] = "Choix d'une photo ou d'un fichier",
            ["nav"] = "Menu de navigation",
            ["net"] = "Problèmes de réseau",
            ["objectives"] = "Objectifs de l'école",
            ["paylink"] = "Lien de paiement WhatsApp",
            ["payment"] = "Paiements",
            ["phone_reset"] = "Réinitialisation par SMS",
            ["push_test"] = "Test des notifications",
            ["quran"] = "Texte coranique",
            ["register"] = "Création de compte",
            ["report"] = "Signalement",
            ["reportcards"] = "Bulletins",
            ["reset"] = "Réinitialisation du mot de passe",
            ["school_info"] = "Informations de l'école",
            ["school_type"] = "Type d'établissement",
            ["school_users"] = "Utilisateurs de l'école",
            ["setup"] = "Premiers réglages",
            ["splash"] = "Écran de démarrage",
            ["staff_attendance"] = "Présences du personnel",
            ["state"] = "États et statuts",
            ["student"] = "Fiche d'un élève",
            ["students"] = "Liste des élèves",
            ["study_log"] = "Cahier de suivi (accès)",
            ["subjects"] = "Matières",
            ["subscription"] = "Abonnement",
            ["superadmin"] = "Administration de la plateforme",
            ["surveillant"] = "Espace surveillant",
            ["teacher"] = "Espace enseignant",
            ["timetable"] = "Emploi du temps",
            ["topup"] = "Recharge du compte",
            ["transfer"] = "Virements",
            ["tx"] = "Transactions (historique)",
            ["update"] = "Mise à jour de l'application",
            ["wallet"] = "Compte de l'école",
            ["withdraw"] = "Retraits",
        };

        /// <summary>Nom lisible d'une section, ou son nom technique si inconnue.</summary>
        public static string Label(string section)
            => Labels.TryGetValue(section, out var l) ? l : section;

        /// <summary>Nombre de sections nommées — utile pour vérifier la couverture.</summary>
        public static int Count => Labels.Count;

        /// <summary>Les sections nommées, pour contrôler qu'aucune n'a été oubliée.</summary>
        public static IReadOnlyCollection<string> Known => Labels.Keys;
    }
}
