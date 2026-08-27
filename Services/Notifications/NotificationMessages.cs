using Idara.API.Common.Utilities;
using Idara.API.Enums;

namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Un message de notification dans ses deux versions : français et arabe.
    ///
    /// La méthode <see cref="Compose"/> décide à l'envoi si on colle les DEUX
    /// langues dans le même corps (compréhension garantie, mais ~3× plus cher en
    /// SMS car l'arabe force l'encodage UCS-2 = 70 car/segment sur tout le
    /// message), ou une seule langue selon la préférence fiable de l'utilisateur
    /// (<c>User.PreferredLanguage</c>, mise à jour à CHAQUE connexion depuis la
    /// langue réelle de l'app — jamais celle de l'admin qui a créé le compte).
    /// Le mode est piloté par un réglage SuperAdmin (PlatformSettings) → bascule
    /// sans redéploiement ; et le SMS d'identifiants force le bilingue pour un
    /// compte JAMAIS connecté (sa « préférence » n'est qu'un héritage).
    /// </summary>
    public sealed record BilingualMessage(string Fr, string Ar)
    {
        /// <summary>Séparateur visuel entre les deux langues d'un SMS bilingue
        /// (validé utilisateur 2026-08-18). Caractères sûrs partout.</summary>
        public const string BilingualSeparator = "\n\n- - - - - - - - - -\n\n";

        public string Compose(bool bilingual, string preferredLanguage = "fr")
        {
            if (bilingual)
                return Fr + BilingualSeparator + Ar;
            return string.Equals(preferredLanguage, "ar", System.StringComparison.OrdinalIgnoreCase)
                ? Ar
                : Fr;
        }
    }

    /// <summary>
    /// Catalogue centralisé des textes de notification (validés 2026-06-08,
    /// reformulés 2026-08-18). Contraintes SMS respectées : AUCUN accent côté FR
    /// (GSM-7 sûr) ; marque « Idara » TOUJOURS en latin, même en arabe (jamais
    /// إدارة). Pas d'emoji. ⚠️ PAS de préfixe « Idara : » dans les corps
    /// (décision 2026-08-18) : l'expéditeur SMS est déjà « Idara » (signature
    /// Orange) et le titre des push aussi — le répéter mangeait des caractères
    /// facturés sur chaque envoi.
    /// </summary>
    public static class NotificationTemplates
    {
        // ===== Notifications parent (incrément 1) =====

        public static BilingualMessage InvoiceDue(string eleve, long montantFcfa, string periode) => new(
            Fr: $"La mensualite de {eleve} ({montantFcfa} FCFA) pour {periode} est a payer. Reglez sur idara.sn ou sur l'application.",
            Ar: $"قسط {eleve} ({montantFcfa} FCFA) عن {periode} مستحق الدفع. ادفع عبر idara.sn أو عبر التطبيق.");

        public static BilingualMessage PaymentReceived(string eleve, long montantFcfa) => new(
            Fr: $"Paiement de {montantFcfa} FCFA recu pour {eleve}. Merci. Votre recu est disponible dans l'application.",
            Ar: $"تم استلام دفعة {montantFcfa} FCFA لفائدة {eleve}. شكرا. الإيصال متاح في التطبيق.");

        public static BilingualMessage InvoiceOverdue(string eleve, long montantFcfa) => new(
            Fr: $"Rappel : la mensualite de {eleve} ({montantFcfa} FCFA) reste a regler. Reglez sur idara.sn ou sur l'application.",
            Ar: $"تذكير: قسط {eleve} ({montantFcfa} FCFA) ما زال غير مدفوع. ادفع عبر idara.sn أو عبر التطبيق.");

        // ===== Frais d'inscription (2026-08-27) =====
        // La facture d'inscription ne passait par AUCUN SMS : la famille
        // n'apprenait son existence qu'au rappel de retard 7 jours plus tard —
        // avec le mot « mensualite » en plus. Le libellé est dérivé du TYPE de
        // facture (§158), comme partout ailleurs.
        public static BilingualMessage RegistrationFeeDue(string eleve, long montantFcfa) => new(
            Fr: $"Les frais d'inscription de {eleve} ({montantFcfa} FCFA) sont a payer. Reglez sur idara.sn ou sur l'application.",
            Ar: $"رسوم تسجيل {eleve} ({montantFcfa} FCFA) مستحقة الدفع. ادفع عبر idara.sn أو عبر التطبيق.");

        public static BilingualMessage RegistrationOverdue(string eleve, long montantFcfa) => new(
            Fr: $"Rappel : les frais d'inscription de {eleve} ({montantFcfa} FCFA) restent a regler. Reglez sur idara.sn ou sur l'application.",
            Ar: $"تذكير: رسوم تسجيل {eleve} ({montantFcfa} FCFA) ما زالت غير مدفوعة. ادفع عبر idara.sn أو عبر التطبيق.");

        // ===== Rappel AVANT la date limite (2026-08-27) =====
        // Part entre J-2 et le jour J, UNIQUEMENT si la fenêtre de paiement est
        // assez longue (cf. OverdueInvoiceReminderJob) — sinon il doublerait le
        // SMS d'émission parti la veille. La date est celle du serveur (§151).
        public static BilingualMessage PaymentDueSoon(
            string eleve, long montantFcfa, DateTime limite, InvoiceType type)
        {
            var date = $"{limite:dd/MM}";
            return type == InvoiceType.Registration
                ? new(
                    Fr: $"Les frais d'inscription de {eleve} ({montantFcfa} FCFA) sont a regler avant le {date}. Reglez sur idara.sn ou sur l'application.",
                    Ar: $"رسوم تسجيل {eleve} ({montantFcfa} FCFA) يجب دفعها قبل {date}. ادفع عبر idara.sn أو عبر التطبيق.")
                : new(
                    Fr: $"La mensualite de {eleve} ({montantFcfa} FCFA) est a regler avant le {date}. Reglez sur idara.sn ou sur l'application.",
                    Ar: $"قسط {eleve} ({montantFcfa} FCFA) يجب دفعه قبل {date}. ادفع عبر idara.sn أو عبر التطبيق.");
        }

        // ===== Écoles en montant LIBRE (2026-08-27) =====
        // Ce mode ne génère AUCUNE facture : sans ce rappel mensuel, les
        // familles de ces daara ne recevaient jamais rien. Pas de montant (il
        // est au choix de la famille), pas de notion de retard. `eleve` null =
        // plusieurs enfants → la formule générique DANS chaque langue (jamais
        // du français inséré dans le corps arabe).
        public static BilingualMessage FreePaymentDue(string? eleve, string periode)
        {
            var fr = string.IsNullOrWhiteSpace(eleve) ? "vos enfants" : eleve;
            var ar = string.IsNullOrWhiteSpace(eleve) ? "أبنائكم" : eleve;
            return new(
                Fr: $"Le paiement de {periode} pour {fr} est attendu. Payez le montant de votre choix sur idara.sn ou sur l'application.",
                Ar: $"دفعة {periode} لفائدة {ar} في انتظار السداد. ادفع المبلغ الذي تختاره عبر idara.sn أو عبر التطبيق.");
        }

        // ===== Annulation d'un encaissement en especes (2026-08-27) =====
        // Sans ce SMS, la famille recevait « Paiement recu » puis, si la
        // direction annulait (erreur de saisie), n'apprenait le retour de sa
        // dette qu'au rappel de retard — incomprehensible pour elle.
        public static BilingualMessage CashPaymentCancelled(string eleve, long montantFcfa) => new(
            Fr: $"Le paiement de {montantFcfa} FCFA enregistre pour {eleve} a ete annule par votre ecole. Le montant reste a payer. Contactez l'ecole en cas de question.",
            Ar: $"تم الغاء دفعة {montantFcfa} FCFA المسجلة لفائدة {eleve} من طرف المدرسة. المبلغ ما زال مستحقا. تواصلوا مع المدرسة عند الحاجة.");

        // ===== Envoi des identifiants par SMS (déclenché par le MODAL, jamais
        // automatique) =====
        // Le SMS ne part QUE quand l'école appuie sur le bouton « SMS » du modal
        // récap (décision produit 2026-08-18 : le SMS est un canal EN PLUS, le
        // choix WhatsApp/SMS/Copier reste à l'école). Un seul template neutre
        // pour l'invitation ET la régénération de code. Format « fiche » (numéro
        // et code chacun sur leur ligne — faciles à recopier pour un public
        // non-tech), validé utilisateur 2026-08-18.
        public static BilingualMessage CredentialsSms(
            string nom, string phone, string code)
        {
            var local = LocalPhone(phone);
            var prenom = string.IsNullOrWhiteSpace(nom) ? "" : " " + nom.Trim();
            return new(
                Fr: $"Salam{prenom},\nVoici vos identifiants Idara :\nNumero : {local}\nCode : {code}\nConnectez-vous sur idara.sn ou sur l'application.",
                Ar: $"السلام عليكم{prenom}،\nإليك بيانات دخولك إلى Idara:\nالرقم: {local}\nالرمز: {code}\nسجّل الدخول على idara.sn أو عبر التطبيق.");
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
            Fr: $"Votre code est {code}. Il expire dans 10 minutes. Ne le partagez avec personne.",
            Ar: $"رمزك هو {code}. ينتهي خلال 10 دقائق. لا تشاركه مع أحد.");

        // ===== Notifications ÉCOLE (push uniquement) =====
        // Paiement reçu côté école : prévient l'admin + le personnel.
        public static BilingualMessage PaymentReceivedSchool(string eleve, long montantFcfa) => new(
            Fr: $"Paiement de {montantFcfa} FCFA recu pour {eleve}. Votre solde a ete credite.",
            Ar: $"تم استلام دفعة {montantFcfa} FCFA لفائدة {eleve}. تم إضافة المبلغ إلى رصيدك.");

        // Rechargement du wallet école (topup) : pas d'élève, c'est l'école qui
        // alimente son propre solde.
        public static BilingualMessage WalletTopupReceived(long montantFcfa) => new(
            Fr: $"Recharge de {montantFcfa} FCFA recue. Votre solde a ete credite.",
            Ar: $"تم استلام شحن بمبلغ {montantFcfa} FCFA. تم إضافة المبلغ إلى رصيدك.");

        // Auto-ajustement de palier à la facturation : l'effectif de l'école a
        // dépassé le plafond de son plan, on l'a remontée au plan adapté.
        public static BilingualMessage SubscriptionPlanUpgraded(
            string nouveauPlan, int effectif, long montantFcfa) => new(
            Fr: $"Votre effectif ({effectif} eleves) depasse votre ancien plan. Vous etes passe au plan {nouveauPlan} ({montantFcfa} FCFA).",
            Ar: $"عدد تلاميذكم ({effectif}) تجاوز خطتكم السابقة. تم ترقيتكم إلى خطة {nouveauPlan} ({montantFcfa} FCFA).");

        // Rappel de renouvellement d'abonnement (push) quelques jours AVANT
        // l'échéance. Deux variantes selon que le wallet couvre déjà le
        // prélèvement (rassurant, aucune action) ou non (inciter à recharger).
        public static BilingualMessage SubscriptionDueSoon(long montantFcfa, DateTime dueDate, bool walletCovers) =>
            walletCovers
                ? new(
                    Fr: $"Votre abonnement de {montantFcfa} FCFA sera preleve automatiquement de votre wallet le {dueDate:dd/MM}. Aucune action requise.",
                    Ar: $"سيُخصم اشتراككم بمبلغ {montantFcfa} FCFA تلقائيا من محفظتكم يوم {dueDate:dd/MM}. لا يلزم أي إجراء.")
                : new(
                    Fr: $"Votre abonnement de {montantFcfa} FCFA sera preleve le {dueDate:dd/MM}. Solde insuffisant : rechargez votre wallet pour eviter l'interruption.",
                    Ar: $"اشتراككم بمبلغ {montantFcfa} FCFA سيُخصم يوم {dueDate:dd/MM}. رصيدكم غير كاف: اشحنوا محفظتكم لتفادي توقف الخدمة.");

        // Prélèvement d'abonnement RÉUSSI (push, après coup) : confirme le débit et
        // la date de validité, en plus de la facture PDF envoyée par email.
        public static BilingualMessage SubscriptionCharged(long montantFcfa, DateTime nextBilling) => new(
            Fr: $"Abonnement de {montantFcfa} FCFA preleve de votre wallet. Votre compte est actif jusqu'au {nextBilling:dd/MM}.",
            Ar: $"تم خصم اشتراك بمبلغ {montantFcfa} FCFA من محفظتكم. حسابكم فعّال حتى {nextBilling:dd/MM}.");

        // Don reçu côté école (push) : prévient l'admin + le personnel. Le nom du
        // donateur est fourni déjà formaté par l'appelant (identité toujours visible).
        public static BilingualMessage DonationReceivedSchool(string donateur, long montantFcfa) => new(
            Fr: $"Don de {montantFcfa} FCFA recu de {donateur}. Votre solde a ete credite.",
            Ar: $"تبرع بمبلغ {montantFcfa} FCFA من {donateur}. تم إضافة المبلغ إلى رصيدك.");

        // Remerciement au donateur (push) après confirmation de son don.
        public static BilingualMessage DonationThanks(long montantFcfa, string ecole) => new(
            Fr: $"Merci pour votre don de {montantFcfa} FCFA a {ecole}. Votre recu est disponible dans l'application.",
            Ar: $"شكرا على تبرعك بمبلغ {montantFcfa} FCFA لفائدة {ecole}. الإيصال متاح في التطبيق.");

        // Retrait/transfert effectué : prévient l'admin uniquement.
        public static BilingualMessage WithdrawalDone(long montantFcfa) => new(
            Fr: $"Votre retrait de {montantFcfa} FCFA a ete effectue avec succes.",
            Ar: $"تم تنفيذ سحبك بمبلغ {montantFcfa} FCFA بنجاح.");

        // ===== Notification BÉNÉFICIAIRE d'un transfert (SMS, + push si compte) =====
        // Envoyée au DESTINATAIRE d'un virement du daara (salaire enseignant,
        // loyer, fournisseur…) quand le décaissement est confirmé `completed`.
        // Chaque corps porte le nom du daara dans SA langue quand il existe
        // (règle R4 §135 : un seul nom renseigné occupe les deux). Les marques
        // (Wave / Orange Money) restent en latin, même en arabe.
        public static BilingualMessage TransferReceived(
            long montantFcfa, PaymentOperator operateur, string? daaraFr, string? daaraAr)
        {
            var name = SchoolDisplayName.From(daaraFr, daaraAr);
            var op = operateur switch
            {
                PaymentOperator.Wave => "Wave",
                PaymentOperator.Orange => "Orange Money",
                _ => "Mobile Money"
            };
            var fr = name.Fr ?? name.Ar ?? "votre daara";
            var ar = name.Ar ?? name.Fr ?? "داراكم";
            return new(
                Fr: $"Vous avez recu un transfert de {montantFcfa} FCFA par {op} de la part de {fr}.",
                Ar: $"لقد استلمت تحويلا بمبلغ {montantFcfa} FCFA عبر {op} من طرف {ar}.");
        }

        // Retrait échoué (fonds restitués) : prévient l'admin uniquement.
        public static BilingualMessage WithdrawalFailed(long montantFcfa) => new(
            Fr: $"Votre retrait de {montantFcfa} FCFA n'a pas abouti. Les fonds ont ete restitues a votre solde.",
            Ar: $"لم يكتمل سحبك بمبلغ {montantFcfa} FCFA. تمت إعادة المبلغ إلى رصيدك.");

        // ===== Suivi de l'enfant (push uniquement, vers les parents) =====
        public static BilingualMessage ChildJournalUpdated(string eleve) => new(
            Fr: $"Le journal du jour de {eleve} est disponible dans l'application.",
            Ar: $"يومية {eleve} لهذا اليوم متاحة في التطبيق.");

        public static BilingualMessage ChildReportCardReady(string eleve) => new(
            Fr: $"Le bulletin de {eleve} est disponible dans l'application.",
            Ar: $"كشف نقاط {eleve} متاح في التطبيق.");

        public static BilingualMessage ChildAbsent(string eleve) => new(
            Fr: $"{eleve} a ete marque(e) absent(e) aujourd'hui.",
            Ar: $"تم تسجيل غياب {eleve} اليوم.");

        // Fin d'un cycle de 22 jours de suivi Coran : le récapitulatif est dispo.
        public static BilingualMessage ChildCoranCycleReady(string eleve) => new(
            Fr: $"Le suivi Coran de {eleve} (cycle termine) est disponible dans l'application.",
            Ar: $"متابعة القرآن لـ {eleve} (انتهت الدورة) متاحة في التطبيق.");
    }
}
