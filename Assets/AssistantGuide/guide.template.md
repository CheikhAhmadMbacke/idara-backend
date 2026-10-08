# Guide d'utilisation d'Idara — espace école (direction et personnel)

Les libellés entre « » sont EXACTEMENT ceux affichés à l'écran, dans la langue de
l'application de l'utilisateur. Cite-les tels quels, entre « », même si tu réponds
dans l'autre langue : c'est ce qu'il lit sur son téléphone.
La barre du BAS porte cinq onglets : «{{nav.home}}», «{{nav.students}}», «{{nav.today}}»,
«{{nav.money}}», «{{nav.settings}}». En HAUT à droite : le bouton de langue et le menu ⋮.

## navigation
Les cinq onglets du bas, et ce qu'on y trouve :
1. «{{nav.home}}» : les compteurs de l'école, les alertes du jour, et la carte «{{assistant.title}}» (boutons «{{assistant.speak}}» et «{{assistant.write}}»).
2. «{{nav.students}}» : la liste des élèves, la recherche «{{students.search}}», le bouton «{{students.add}}», et dans le menu ⋮ en haut : «{{import.tile}}».
3. «{{nav.today}}» : section «{{dashboard.roll_call_section}}» avec «{{dashboard.attendance}}» ; section «{{dashboard.study_section}}» avec «{{dashboard.study_log}}», «{{dashboard.grades}}», «{{dashboard.report_cards}}».
4. «{{nav.money}}» : section «{{dashboard.money_in_section}}» avec «{{dashboard.payments_overview}}» et «{{finance.title}}» ; section «{{dashboard.money_setup_section}}» avec «{{dashboard.payment_settings}}» et «{{subscription.menu_my_sub}}».
5. «{{nav.settings}}» : «{{setup.title}}», section «{{dashboard.classes_section}}» («{{dashboard.manage_classes}}», «{{dashboard.manage_subjects}}», «{{dashboard.assignments}}», «{{dashboard.timetable}}»), section «{{dashboard.people_section}}» («{{school_users.title}}»), et «{{school_info.title}}».

## add_student
Ajouter un élève à la main :
1. Onglet «{{nav.students}}» (barre du bas).
2. Bouton «{{students.add}}».
3. Remplir au minimum «{{student.first_name}}» et le nom ; choisir la «{{student.class_name}}».
4. Pour créer le compte du parent : «{{student.add_guardian}}», puis son nom et «{{student.guardian_phone}}» (numéro sénégalais). Le parent reçoit un code de connexion.
5. «{{student.registration_fee_label}}» : le montant à facturer (vider le champ pour ne rien facturer). «{{student.custom_fee_label}}» : seulement si l'élève paie un tarif différent de sa classe.
6. Bouton «{{common.create}}» en bas de l'écran.
Raccourci : l'assistant peut aussi préparer l'inscription si on lui dicte l'élève.

## import_students
Importer toute une liste d'élèves d'un coup :
1. Onglet «{{nav.students}}».
2. Menu ⋮ en haut à droite → «{{import.tile}}».
3. Avec un fichier Excel : «{{import.download_template}}» pour avoir le bon modèle, le remplir, puis «{{import.pick_file}}».
4. Depuis un cahier ou un registre papier : section «{{import.photo_title}}» → «{{import.photo_add}}», photographier les pages (ou choisir un PDF).
5. Vérifier l'écran «{{import.preview_title}}» (élèves, responsables, problèmes signalés), puis «{{import.confirm_action}}».
Rien n'est enregistré avant «{{import.confirm_action}}». Le personnel s'importe de la même façon : onglet «{{nav.settings}}» → «{{school_users.title}}» → «{{staff_import.tile}}».

## classes_and_fees
Créer une classe et fixer sa mensualité :
1. Onglet «{{nav.settings}}» → «{{dashboard.manage_classes}}».
2. Bouton «{{classes.add}}» : «{{classes.name}}», et «{{classes.monthly_fee}}» si la classe a son propre tarif.
3. Bouton «{{common.create}}».
Le tarif de toutes les classes se voit aussi dans : onglet «{{nav.money}}» → «{{dashboard.payment_settings}}» → «{{dashboard.class_fees}}».

## staff_accounts
Créer le compte d'un enseignant ou d'un membre du personnel :
1. Onglet «{{nav.settings}}» → «{{school_users.title}}».
2. Bouton «{{school_users.add_user}}» : l'écran «{{invite.title}}» s'ouvre.
3. Choisir la «{{invite.function}}», saisir le nom et le «{{invite.phone}}».
4. Bouton «{{invite.send}}» : la personne reçoit ses identifiants par SMS.
Pour affecter un enseignant à une classe : onglet «{{nav.settings}}» → «{{dashboard.assignments}}».

## who_has_not_paid
Voir qui a payé et qui est en retard ce mois-ci :
1. Onglet «{{nav.money}}» → «{{dashboard.payments_overview}}».
2. Onglet «{{payment.tracking_tab}}» : la liste du mois, avec les filtres «{{payment.roster_overdue}}», «{{payment.roster_pending}}», «{{payment.roster_paid}}».
Attention : en mode «{{payment.mode_free}}», l'école n'a pas de mensualités ; chaque famille paie ce qu'elle veut, et la liste ne peut pas dire qui « doit ».
Raccourci : demander à l'assistant « Qui n'a pas payé ce mois-ci ? ».

## record_cash_payment
Enregistrer un paiement reçu HORS d'Idara (espèces, ou Wave / Orange Money envoyé directement sur le numéro du daara) :
1. Onglet «{{nav.money}}» → «{{dashboard.payments_overview}}» → onglet «{{payment.tracking_tab}}».
2. Sur la ligne de l'élève, le menu ⋮ → «{{cash.action}}».
3. Écran «{{cash.title}}» : «{{cash.amount_label}}», puis «{{cash.submit}}».
Le reçu part par SMS au parent. Les paiements faits DANS Idara (espace parent ou lien de paiement) s'enregistrent tout seuls : il ne faut pas les ressaisir.

## send_payment_link
Envoyer à un parent son lien de paiement Wave :
1. Onglet «{{nav.money}}» → «{{dashboard.payments_overview}}» → onglet «{{payment.tracking_tab}}».
2. Sur la ligne de l'élève, le menu ⋮ → «{{paylink.action}}».
3. Partager le lien (WhatsApp, SMS). Le parent paie en un geste, le reçu et la mise à jour sont automatiques.
L'élève doit avoir un responsable avec un numéro de téléphone.

## payment_settings
Régler la façon dont l'école facture :
1. Onglet «{{nav.money}}» → «{{dashboard.payment_settings}}».
2. Choisir «{{payment.mode_fixed}}» (une mensualité par élève, avec relances automatiques) ou «{{payment.mode_free}}» (chaque famille paie le montant qu'elle veut).
3. En «{{payment.mode_fixed}}» : «{{payment.general_fee_label}}», «{{payment.opening_day_label}}», «{{payment.deadline_day_label}}», et les «{{dashboard.class_fees}}».
4. «{{payment.registration_fee_label}}» : le montant proposé par défaut à chaque nouvel élève.
5. Bouton «{{common.save}}».

## withdraw_money
Retirer l'argent reçu par Wave vers un numéro :
1. Onglet «{{nav.money}}» → «{{dashboard.payments_overview}}» → onglet «{{payment.wallet_tab}}».
2. Bouton «{{withdraw.title}}» ; l'application redemande le mot de passe.
3. «{{withdraw.withdraw_action}}», puis les trois étapes «{{withdraw.step_how_much}}», «{{withdraw.step_for_whom}}» («{{withdraw.my_number}}» ou un bénéficiaire), «{{withdraw.step_check}}».
4. Valider. Les frais de retrait s'ajoutent au montant envoyé ; l'écran les montre avant de valider.

## subscription_and_plans
Voir son abonnement, payer un plan tout de suite, ou changer de plan :
1. Onglet «{{nav.money}}» → «{{subscription.menu_my_sub}}».
2. Pour payer un plan TOUT DE SUITE (finir l'essai plus tôt, ou monter de plan en cours de mois) : «{{activate.button}}», choisir le plan ; l'écran dit jusqu'à quand le paiement couvre ; puis «{{activate.pay_wallet}}» si le solde suffit, sinon payer avec Wave. Le plan s'active dès le paiement. On paie le mois en cours jusqu'au prochain 8, au prix plein, sans prorata ; les jours d'essai restants ne s'ajoutent pas.
3. Pour changer de plan au PROCHAIN prélèvement (notamment pour un plan moins cher) : «{{subscription.change_plan}}», choisir le plan, puis «{{subscription.confirm_change}}».
L'assistant est inclus dans le plan Pro (400 commandes par mois) et illimité dans le plan Grand, dès que le plan est payé. Les prix à jour sont sur idara.sn/plans.

## attendance
Faire l'appel :
1. Onglet «{{nav.today}}» → «{{dashboard.attendance}}».
2. Marquer chaque élève, puis enregistrer. L'appel fonctionne même sans connexion internet.

## study_and_grades
Le suivi pédagogique :
- Mémorisation du Coran et journal : onglet «{{nav.today}}» → «{{dashboard.study_log}}».
- Notes : onglet «{{nav.today}}» → «{{dashboard.grades}}».
- Bulletins PDF : onglet «{{nav.today}}» → «{{dashboard.report_cards}}».

## assistant_credits
L'assistant et ses commandes :
1. Onglet «{{nav.home}}» → carte «{{assistant.title}}» → «{{assistant.speak}}» (à la voix) ou «{{assistant.write}}».
2. Le solde de commandes s'affiche en haut de l'assistant ; bouton «{{assistant.buy}}» pour en acheter par Wave.
Une commande = une demande traitée. Confirmer une carte est gratuit. Une demande hors sujet n'est pas décomptée.

## account_and_language
- Changer la langue (français / arabe) : le bouton de langue en haut à droite de l'écran.
- Changer son mot de passe : menu ⋮ en haut à droite → «{{changepw.title}}».
- Signaler un problème à l'équipe Idara : menu ⋮ en haut à droite → «{{report.title}}».

## school_info
Modifier les informations de l'école (nom en français et en arabe, adresse, téléphone, logo) :
1. Onglet «{{nav.settings}}» → «{{school_info.title}}».
2. Modifier, puis enregistrer.
