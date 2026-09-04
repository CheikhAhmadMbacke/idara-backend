using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Les colonnes officielles de chaque import — <b>source unique</b>.
    ///
    /// <para>Elles servent à trois endroits qui doivent dire exactement la même
    /// chose : l'en-tête du fichier modèle téléchargé par l'école, la consigne
    /// donnée à l'IA qui lit un cahier photographié, et les intitulés que le
    /// parseur sait reconnaître. Les laisser recopiées dans chacun aurait
    /// garanti qu'un jour l'IA produise une colonne que le parseur ignore en
    /// silence — deuxième occurrence du piège du §140.</para>
    ///
    /// <para>⚠️ L'ORDRE compte : c'est celui des cellules rendues par l'IA.</para>
    /// </summary>
    public static class ImportColumns
    {
        public static readonly string[] Students =
        {
            "Prénom", "Nom", "Classe", "Date de naissance", "Sexe", "Régime",
            "Tarif", "Frais d'inscription", "Prénom du responsable",
            "Nom du responsable", "Téléphone du responsable", "Lien de parenté",
            "Adresse", "Remarques",
        };

        public static readonly string[] Staff =
        {
            "Nom complet", "Téléphone", "Fonction", "Intitulé du poste",
            "Accès à l'application", "Email",
        };

        public static string[] For(ImportKind kind) => kind switch
        {
            ImportKind.Staff => Staff,
            _ => Students,
        };

        /// <summary>
        /// Ce que chaque colonne attend, dit à quelqu'un qui recopie un cahier.
        /// Sert de consigne à l'IA — et c'est cette consigne, pas le code, qui
        /// décide de la qualité de la lecture.
        /// </summary>
        public static string Guidance(ImportKind kind) => kind switch
        {
            ImportKind.Staff => """
                - Nom complet : prénom et nom dans la même case, tels qu'écrits.
                - Téléphone : les chiffres exactement comme ils sont écrits, espaces compris.
                - Fonction : recopier le mot employé (Enseignant, Maître, Oustaz, Personnel,
                  Surveillant, Cuisinière, Gardien…). Ne pas traduire, ne pas normaliser.
                - Intitulé du poste : le titre précis s'il diffère de la fonction.
                - Accès à l'application : « Oui » ou « Non » seulement si le cahier le dit.
                - Email : uniquement s'il est écrit.
                """,
            _ => """
                - Prénom / Nom : séparés si le cahier les sépare. Si le cahier n'a qu'une
                  colonne « Nom et prénom », mettre le premier mot en Prénom et le reste en Nom.
                - Classe : le nom de la classe ou de la halaqa tel qu'écrit (CI, CP, Halaqa 2…).
                  Si la classe est écrite en titre au-dessus d'un groupe de lignes, la reporter
                  sur CHAQUE ligne du groupe.
                - Date de naissance : recopier telle quelle (03/04/2015, 2015, 3 avril 2015…).
                  Ne jamais convertir ni compléter une année manquante.
                - Sexe : M, F, ou le mot écrit (garçon, fille…).
                - Régime : Interne, Demi-interne, Externe — seulement si le cahier le dit.
                - Tarif / Frais d'inscription : les chiffres seulement, sans « FCFA ».
                - Responsable : prénom, nom, téléphone, lien de parenté (Père, Mère, Tuteur…).
                - Téléphone du responsable : ⚠️ recopier chiffre par chiffre. C'est la donnée
                  la plus critique de la page : un seul chiffre faux donne un parent injoignable.
                - Adresse : quartier ou ville.
                - Remarques : tout ce qui ne rentre nulle part. Si une ligne est RAYÉE ou barrée,
                  écrire « rayé » ici (ne pas supprimer la ligne : c'est à l'école de décider).
                """,
        };
    }
}
