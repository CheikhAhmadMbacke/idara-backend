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

        // ===== Invitation par numéro (incrément 2) =====
        // Le code à 6 chiffres EST le mot de passe initial (non-expirant). Le
        // parent se connecte avec son numero + ce code, puis pourra le changer
        // dans l'app.
        public static BilingualMessage InviteWelcome(
            string prenom, string ecole, string fonctionFr, string fonctionAr,
            string phone, string code) => new(
            Fr: $"Idara : bienvenue {prenom}. {ecole} vous a ajoute comme {fonctionFr}. Connectez-vous sur idara.sn avec votre numero {phone} et le code {code}. Vous pourrez le changer dans l'app.",
            Ar: $"Idara: مرحبا {prenom}، أضافتك {ecole} بصفة {fonctionAr}. سجّل الدخول على idara.sn برقمك {phone} والرمز {code}. يمكنك تغييره لاحقا في التطبيق.");

        // ===== Régénération du code d'accès (reset par l'école, sans SMS auto) =====
        // L'école régénère un nouveau code à 6 chiffres pour un parent/enseignant
        // qui a oublié le sien, et le lui recommunique (modal récap + WhatsApp).
        public static BilingualMessage AccessCodeReset(
            string prenom, string ecole, string phone, string code) => new(
            Fr: $"Idara : {prenom}, {ecole} a regenere votre code d'acces. Connectez-vous sur idara.sn avec votre numero {phone} et le nouveau code {code}. Vous pourrez le changer dans l'app.",
            Ar: $"Idara: {prenom}، أعادت {ecole} إنشاء رمز دخولك. سجّل الدخول على idara.sn برقمك {phone} والرمز الجديد {code}. يمكنك تغييره لاحقا في التطبيق.");

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
    }
}
