namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Un message de notification dans ses deux versions : français et arabe.
    ///
    /// La méthode <see cref="Compose"/> décide à l'envoi si on colle les DEUX
    /// langues dans le même corps (compréhension garantie, mais ~3× plus cher en
    /// SMS car l'arabe force l'encodage UCS-2 = 70 car/segment sur tout le
    /// message), ou une seule langue selon la préférence fiable de l'utilisateur
    /// (<c>User.PreferredLanguage</c>). Le mode est piloté par un réglage
    /// SuperAdmin (PlatformSettings) → bascule sans redéploiement.
    /// </summary>
    public sealed record BilingualMessage(string Fr, string Ar)
    {
        public string Compose(bool bilingual, string preferredLanguage = "fr")
        {
            if (bilingual)
                return Fr + "\n\n" + Ar;
            return string.Equals(preferredLanguage, "ar", System.StringComparison.OrdinalIgnoreCase)
                ? Ar
                : Fr;
        }
    }

    /// <summary>
    /// Catalogue centralisé des textes de notification (validés 2026-06-08).
    /// Contraintes SMS respectées : accents GSM-7 uniquement côté FR (é è à ç ù —
    /// pas de ê î ô â ë ï qui forceraient l'UCS-2) ; marque « Idara » TOUJOURS en
    /// latin, même en arabe (jamais إدارة). Pas d'emoji.
    /// Les libellés de fonction sont fournis déjà localisés par l'appelant.
    /// </summary>
    public static class NotificationTemplates
    {
        // ===== Notifications parent (incrément 1) =====

        public static BilingualMessage InvoiceDue(string eleve, long montantFcfa, string periode) => new(
            Fr: $"Idara : la mensualite de {eleve} ({montantFcfa} FCFA) pour {periode} est a payer. Reglez sur idara.sn.",
            Ar: $"Idara: قسط {eleve} ({montantFcfa} FCFA) عن {periode} مستحق الدفع. ادفع عبر idara.sn.");

        public static BilingualMessage PaymentReceived(string eleve, long montantFcfa) => new(
            Fr: $"Idara : paiement de {montantFcfa} FCFA recu pour {eleve}. Merci. Votre recu est disponible dans l'application.",
            Ar: $"Idara: تم استلام دفعة {montantFcfa} FCFA لفائدة {eleve}. شكرا. الإيصال متاح في التطبيق.");

        public static BilingualMessage InvoiceOverdue(string eleve, long montantFcfa) => new(
            Fr: $"Idara : rappel. La mensualite de {eleve} ({montantFcfa} FCFA) reste a regler. Reglez sur idara.sn.",
            Ar: $"Idara: تذكير. قسط {eleve} ({montantFcfa} FCFA) ما زال غير مدفوع. ادفع عبر idara.sn.");

        // ===== Envoi des identifiants par SMS (déclenché par le MODAL, jamais
        // automatique) =====
        // Le SMS ne part QUE quand l'école appuie sur le bouton « SMS » du modal
        // récap (décision produit 2026-08-18 : le SMS est un canal EN PLUS, le
        // choix WhatsApp/SMS/Copier reste à l'école). Un seul template neutre
        // pour l'invitation ET la régénération de code : dans les deux cas le
        // destinataire reçoit « voici vos identifiants ».
        public static BilingualMessage CredentialsSms(
            string nom, string phone, string code)
        {
            var local = LocalPhone(phone);
            var prenom = string.IsNullOrWhiteSpace(nom) ? "" : " " + nom.Trim();
            return new(
                Fr: $"Idara : Salam{prenom}, voici vos identifiants. Connectez-vous sur idara.sn avec votre numero {local} et le code {code}. Vous pourrez le changer dans l'app.",
                Ar: $"Idara: السلام عليكم{prenom}، إليك بيانات دخولك. سجّل الدخول على idara.sn برقمك {local} والرمز {code}. يمكنك تغييره لاحقا في التطبيق.");
        }

        // ===== Message d'identifiants PARTAGÉ MANUELLEMENT (WhatsApp/Copier) =====
        // Volontairement court et EN FRANÇAIS UNIQUEMENT : c'est le texte que
        // l'école copie-colle à ses parents/enseignants (modal récap, §94). Les
        // retours terrain ont jugé l'ancien message bilingue trop chargé. Le
        // bouton « SMS » du modal, lui, passe par le template bilingue
        // CredentialsSms ci-dessus (envoyé par le serveur via l'API Orange).
        // Le numéro est affiché « sans indicatif » (forme locale, plus familière —
        // le login accepte de toute façon les deux formes).
        public static string CredentialShare(string fullName, string phone, string code)
        {
            var local = LocalPhone(phone);
            var nom = string.IsNullOrWhiteSpace(fullName) ? "" : " " + fullName.Trim();
            return $"Salam{nom}, voici vos identifiants pour vous connecter à l'application Idara :\n\n"
                 + $"Numéro de téléphone : {local}\n"
                 + $"Code : {code}";
        }

        /// <summary>Retire l'indicatif +221 / 221 d'un numéro E.164 pour un
        /// affichage local (771234567). Renvoie l'entrée telle quelle sinon.</summary>
        private static string LocalPhone(string phone)
        {
            var p = (phone ?? "").Trim();
            if (p.StartsWith("+221")) return p.Substring(4);
            if (p.StartsWith("221") && p.Length > 9) return p.Substring(3);
            return p;
        }

        // ===== Code OTP (activation / réinitialisation par SMS) =====
        public static BilingualMessage OtpCode(string code) => new(
            Fr: $"Idara : votre code est {code}. Il expire dans 10 minutes. Ne le partagez avec personne.",
            Ar: $"Idara: رمزك هو {code}. ينتهي خلال 10 دقائق. لا تشاركه مع أحد.");

        // ===== Notifications ÉCOLE (push uniquement) =====
        // Paiement reçu côté école : prévient l'admin + le personnel.
        public static BilingualMessage PaymentReceivedSchool(string eleve, long montantFcfa) => new(
            Fr: $"Idara : paiement de {montantFcfa} FCFA recu pour {eleve}. Votre solde a ete credite.",
            Ar: $"Idara: تم استلام دفعة {montantFcfa} FCFA لفائدة {eleve}. تم إضافة المبلغ إلى رصيدك.");

        // Rechargement du wallet école (topup) : pas d'élève, c'est l'école qui
        // alimente son propre solde.
        public static BilingualMessage WalletTopupReceived(long montantFcfa) => new(
            Fr: $"Idara : recharge de {montantFcfa} FCFA recue. Votre solde a ete credite.",
            Ar: $"Idara: تم استلام شحن بمبلغ {montantFcfa} FCFA. تم إضافة المبلغ إلى رصيدك.");

        // Auto-ajustement de palier à la facturation : l'effectif de l'école a
        // dépassé le plafond de son plan, on l'a remontée au plan adapté.
        public static BilingualMessage SubscriptionPlanUpgraded(
            string nouveauPlan, int effectif, long montantFcfa) => new(
            Fr: $"Idara : votre effectif ({effectif} eleves) depasse votre ancien plan. Vous etes passe au plan {nouveauPlan} ({montantFcfa} FCFA).",
            Ar: $"Idara: عدد تلاميذكم ({effectif}) تجاوز خطتكم السابقة. تم ترقيتكم إلى خطة {nouveauPlan} ({montantFcfa} FCFA).");

        // Rappel de renouvellement d'abonnement (push) quelques jours AVANT
        // l'échéance. Deux variantes selon que le wallet couvre déjà le
        // prélèvement (rassurant, aucune action) ou non (inciter à recharger).
        public static BilingualMessage SubscriptionDueSoon(long montantFcfa, DateTime dueDate, bool walletCovers) =>
            walletCovers
                ? new(
                    Fr: $"Idara : votre abonnement de {montantFcfa} FCFA sera preleve automatiquement de votre wallet le {dueDate:dd/MM}. Aucune action requise.",
                    Ar: $"Idara: سيُخصم اشتراككم بمبلغ {montantFcfa} FCFA تلقائيا من محفظتكم يوم {dueDate:dd/MM}. لا يلزم أي إجراء.")
                : new(
                    Fr: $"Idara : votre abonnement de {montantFcfa} FCFA sera preleve le {dueDate:dd/MM}. Solde insuffisant : rechargez votre wallet pour eviter l'interruption.",
                    Ar: $"Idara: اشتراككم بمبلغ {montantFcfa} FCFA سيُخصم يوم {dueDate:dd/MM}. رصيدكم غير كاف: اشحنوا محفظتكم لتفادي توقف الخدمة.");

        // Prélèvement d'abonnement RÉUSSI (push, après coup) : confirme le débit et
        // la date de validité, en plus de la facture PDF envoyée par email.
        public static BilingualMessage SubscriptionCharged(long montantFcfa, DateTime nextBilling) => new(
            Fr: $"Idara : abonnement de {montantFcfa} FCFA preleve de votre wallet. Votre compte est actif jusqu'au {nextBilling:dd/MM}.",
            Ar: $"Idara: تم خصم اشتراك بمبلغ {montantFcfa} FCFA من محفظتكم. حسابكم فعّال حتى {nextBilling:dd/MM}.");

        // Don reçu côté école (push) : prévient l'admin + le personnel. Le nom du
        // donateur est fourni déjà formaté par l'appelant (identité toujours visible).
        public static BilingualMessage DonationReceivedSchool(string donateur, long montantFcfa) => new(
            Fr: $"Idara : don de {montantFcfa} FCFA recu de {donateur}. Votre solde a ete credite.",
            Ar: $"Idara: تبرع بمبلغ {montantFcfa} FCFA من {donateur}. تم إضافة المبلغ إلى رصيدك.");

        // Remerciement au donateur (push) après confirmation de son don.
        public static BilingualMessage DonationThanks(long montantFcfa, string ecole) => new(
            Fr: $"Idara : merci pour votre don de {montantFcfa} FCFA a {ecole}. Votre recu est disponible dans l'application.",
            Ar: $"Idara: شكرا على تبرعك بمبلغ {montantFcfa} FCFA لفائدة {ecole}. الإيصال متاح في التطبيق.");

        // Retrait/transfert effectué : prévient l'admin uniquement.
        public static BilingualMessage WithdrawalDone(long montantFcfa) => new(
            Fr: $"Idara : votre retrait de {montantFcfa} FCFA a ete effectue avec succes.",
            Ar: $"Idara: تم تنفيذ سحبك بمبلغ {montantFcfa} FCFA بنجاح.");

        // Retrait échoué (fonds restitués) : prévient l'admin uniquement.
        public static BilingualMessage WithdrawalFailed(long montantFcfa) => new(
            Fr: $"Idara : votre retrait de {montantFcfa} FCFA n'a pas abouti. Les fonds ont ete restitues a votre solde.",
            Ar: $"Idara: لم يكتمل سحبك بمبلغ {montantFcfa} FCFA. تمت إعادة المبلغ إلى رصيدك.");

        // ===== Suivi de l'enfant (push uniquement, vers les parents) =====
        public static BilingualMessage ChildJournalUpdated(string eleve) => new(
            Fr: $"Idara : le journal du jour de {eleve} est disponible dans l'application.",
            Ar: $"Idara: يومية {eleve} لهذا اليوم متاحة في التطبيق.");

        public static BilingualMessage ChildReportCardReady(string eleve) => new(
            Fr: $"Idara : le bulletin de {eleve} est disponible dans l'application.",
            Ar: $"Idara: كشف نقاط {eleve} متاح في التطبيق.");

        public static BilingualMessage ChildAbsent(string eleve) => new(
            Fr: $"Idara : {eleve} a ete marque(e) absent(e) aujourd'hui.",
            Ar: $"Idara: تم تسجيل غياب {eleve} اليوم.");

        // Fin d'un cycle de 22 jours de suivi Coran : le récapitulatif est dispo.
        public static BilingualMessage ChildCoranCycleReady(string eleve) => new(
            Fr: $"Idara : le suivi Coran de {eleve} (cycle termine) est disponible dans l'application.",
            Ar: $"Idara: متابعة القرآن لـ {eleve} (انتهت الدورة) متاحة في التطبيق.");
    }
}
