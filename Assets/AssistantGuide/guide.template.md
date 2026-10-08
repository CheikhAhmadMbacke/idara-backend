# Guide d'utilisation d'Idara — espace école (direction et personnel)

<!-- Chaque libellé porte l'écran où il DOIT se trouver : {{clé@fichier}}. Le
     contrôle --check vérifie que le bouton y est toujours — un guide qui
     enverrait l'utilisateur chercher une tuile déplacée est refusé. -->
Les libellés entre « » sont EXACTEMENT ceux affichés à l'écran, dans la langue de
l'application de l'utilisateur. Cite-les tels quels, entre « », même si tu réponds
dans l'autre langue : c'est ce qu'il lit sur son téléphone.
La barre du BAS porte cinq onglets : «{{nav.home@core/navigation/nav_destinations.dart}}», «{{nav.students@core/navigation/nav_destinations.dart}}», «{{nav.today@core/navigation/nav_destinations.dart}}»,
«{{nav.money@core/navigation/nav_destinations.dart}}», «{{nav.settings@core/navigation/nav_destinations.dart}}». En HAUT à droite : le bouton de langue et le menu ⋮.

## navigation
Les cinq onglets du bas, et ce qu'on y trouve :
1. «{{nav.home@core/navigation/nav_destinations.dart}}» : les compteurs de l'école, les alertes du jour, et la carte «{{assistant.title@presentation/widgets/assistant_entry_card.dart}}» (boutons «{{assistant.speak@presentation/widgets/assistant_entry_card.dart}}» et «{{assistant.write@presentation/widgets/assistant_entry_card.dart}}»).
2. «{{nav.students@core/navigation/nav_destinations.dart}}» : la liste des élèves, la recherche «{{students.search@presentation/pages/student_list_page.dart}}», le bouton «{{students.add@presentation/pages/student_list_page.dart}}», et dans le menu ⋮ en haut : «{{import.tile@presentation/pages/student_list_page.dart}}».
3. «{{nav.today@core/navigation/nav_destinations.dart}}» : section «{{dashboard.roll_call_section@presentation/pages/shell/school_today_page.dart}}» avec «{{dashboard.attendance@presentation/pages/shell/school_today_page.dart}}» ; section «{{dashboard.study_section@presentation/pages/shell/school_today_page.dart}}» avec «{{dashboard.study_log@presentation/pages/shell/school_today_page.dart}}», «{{dashboard.grades@presentation/pages/shell/school_today_page.dart}}», «{{dashboard.report_cards@presentation/pages/shell/school_today_page.dart}}».
4. «{{nav.money@core/navigation/nav_destinations.dart}}» : section «{{dashboard.money_in_section@presentation/pages/shell/school_money_page.dart}}» avec «{{dashboard.payments_overview@presentation/pages/shell/school_money_page.dart}}» et «{{finance.title@presentation/pages/shell/school_money_page.dart}}» ; section «{{dashboard.money_setup_section@presentation/pages/shell/school_money_page.dart}}» avec «{{dashboard.payment_settings@presentation/pages/shell/school_money_page.dart}}» et «{{subscription.menu_my_sub@presentation/pages/shell/school_money_page.dart}}».
5. «{{nav.settings@core/navigation/nav_destinations.dart}}» : «{{setup.title@presentation/pages/shell/school_settings_page.dart}}», section «{{dashboard.classes_section@presentation/pages/shell/school_settings_page.dart}}» («{{dashboard.manage_classes@presentation/pages/shell/school_settings_page.dart}}», «{{dashboard.manage_subjects@presentation/pages/shell/school_settings_page.dart}}», «{{dashboard.assignments@presentation/pages/shell/school_settings_page.dart}}», «{{dashboard.timetable@presentation/pages/shell/school_settings_page.dart}}»), section «{{dashboard.people_section@presentation/pages/shell/school_settings_page.dart}}» («{{school_users.title@presentation/pages/shell/school_settings_page.dart}}»), et «{{school_info.title@presentation/pages/shell/school_settings_page.dart}}».

## add_student
Ajouter un élève à la main :
1. Onglet «{{nav.students@core/navigation/nav_destinations.dart}}» (barre du bas).
2. Bouton «{{students.add@presentation/pages/student_list_page.dart}}».
3. Remplir au minimum «{{student.first_name@presentation/pages/student_form_page.dart}}» et le nom ; choisir la «{{student.class_name@presentation/pages/student_form_page.dart}}».
4. Pour créer le compte du parent : «{{student.add_guardian@presentation/pages/student_form_page.dart}}», puis son nom et «{{student.guardian_phone@presentation/pages/student_form_page.dart}}» (numéro sénégalais). Le parent reçoit un code de connexion.
5. «{{student.registration_fee_label@presentation/pages/student_form_page.dart}}» : le montant à facturer (vider le champ pour ne rien facturer). «{{student.custom_fee_label@presentation/pages/student_form_page.dart}}» : seulement si l'élève paie un tarif différent de sa classe.
6. Bouton «{{common.create@presentation/pages/student_form_page.dart}}» en bas de l'écran.
Raccourci : l'assistant peut aussi préparer l'inscription si on lui dicte l'élève.

## import_students
Importer toute une liste d'élèves d'un coup :
1. Onglet «{{nav.students@core/navigation/nav_destinations.dart}}».
2. Menu ⋮ en haut à droite → «{{import.tile@presentation/pages/student_list_page.dart}}».
3. Avec un fichier Excel : «{{import.download_template@presentation/pages/student_import_page.dart}}» pour avoir le bon modèle, le remplir, puis «{{import.pick_file@presentation/pages/student_import_page.dart}}».
4. Depuis un cahier ou un registre papier : section «{{import.photo_title@presentation/pages/student_import_page.dart}}» → «{{import.photo_add@presentation/pages/student_import_page.dart}}», photographier les pages (ou choisir un PDF).
5. Vérifier l'écran «{{import.preview_title@presentation/pages/student_import_page.dart}}» (élèves, responsables, problèmes signalés), puis «{{import.confirm_action@presentation/pages/student_import_page.dart}}».
Rien n'est enregistré avant «{{import.confirm_action@presentation/pages/student_import_page.dart}}». Le personnel s'importe de la même façon : onglet «{{nav.settings@core/navigation/nav_destinations.dart}}» → «{{school_users.title@presentation/pages/shell/school_settings_page.dart}}» → «{{staff_import.tile@presentation/pages/school_users_page.dart}}».

## classes_and_fees
Créer une classe et fixer sa mensualité :
1. Onglet «{{nav.settings@core/navigation/nav_destinations.dart}}» → «{{dashboard.manage_classes@presentation/pages/shell/school_settings_page.dart}}».
2. Bouton «{{classes.add@presentation/pages/class_list_page.dart}}» : «{{classes.name@presentation/pages/class_list_page.dart}}», et «{{classes.monthly_fee@presentation/pages/class_list_page.dart}}» si la classe a son propre tarif.
3. Bouton «{{common.create@presentation/pages/class_list_page.dart}}».
Le tarif de toutes les classes se voit aussi dans : onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{dashboard.payment_settings@presentation/pages/shell/school_money_page.dart}}» → «{{dashboard.class_fees@presentation/pages/payment_settings_page.dart}}».

## staff_accounts
Créer le compte d'un enseignant ou d'un membre du personnel :
1. Onglet «{{nav.settings@core/navigation/nav_destinations.dart}}» → «{{school_users.title@presentation/pages/shell/school_settings_page.dart}}».
2. Bouton «{{school_users.add_user@presentation/pages/school_users_page.dart}}» : l'écran «{{invite.title@presentation/pages/invite_user_page.dart}}» s'ouvre.
3. Choisir la «{{invite.function@presentation/pages/invite_user_page.dart}}», saisir le nom et le «{{invite.phone@presentation/pages/invite_user_page.dart}}».
4. Bouton «{{invite.send@presentation/pages/invite_user_page.dart}}» : la personne reçoit ses identifiants par SMS.
Pour affecter un enseignant à une classe : onglet «{{nav.settings@core/navigation/nav_destinations.dart}}» → «{{dashboard.assignments@presentation/pages/shell/school_settings_page.dart}}».

## who_has_not_paid
Voir qui a payé et qui est en retard ce mois-ci :
1. Onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{dashboard.payments_overview@presentation/pages/shell/school_money_page.dart}}».
2. Onglet «{{payment.tracking_tab@presentation/pages/school_payments_overview_page.dart}}» : la liste du mois, avec les filtres «{{payment.roster_overdue@presentation/pages/school_payments_overview_page.dart}}», «{{payment.roster_pending@presentation/pages/school_payments_overview_page.dart}}», «{{payment.roster_paid@presentation/pages/school_payments_overview_page.dart}}».
Attention : en mode «{{payment.mode_free@presentation/pages/payment_settings_page.dart}}», l'école n'a pas de mensualités ; chaque famille paie ce qu'elle veut, et la liste ne peut pas dire qui « doit ».
Raccourci : demander à l'assistant « Qui n'a pas payé ce mois-ci ? ».

## record_cash_payment
Enregistrer un paiement reçu HORS d'Idara (espèces, ou Wave / Orange Money envoyé directement sur le numéro du daara) :
1. Onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{dashboard.payments_overview@presentation/pages/shell/school_money_page.dart}}» → onglet «{{payment.tracking_tab@presentation/pages/school_payments_overview_page.dart}}».
2. Sur la ligne de l'élève, le menu ⋮ → «{{cash.action@presentation/pages/school_payments_overview_page.dart}}».
3. Écran «{{cash.title@core/widgets/cash_payment_sheet.dart}}» : «{{cash.amount_label@core/widgets/cash_payment_sheet.dart}}», puis «{{cash.submit@core/widgets/cash_payment_sheet.dart}}».
Le reçu part par SMS au parent. Les paiements faits DANS Idara (espace parent ou lien de paiement) s'enregistrent tout seuls : il ne faut pas les ressaisir.

## send_payment_link
Envoyer à un parent son lien de paiement Wave :
1. Onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{dashboard.payments_overview@presentation/pages/shell/school_money_page.dart}}» → onglet «{{payment.tracking_tab@presentation/pages/school_payments_overview_page.dart}}».
2. Sur la ligne de l'élève, le menu ⋮ → «{{paylink.action@presentation/pages/school_payments_overview_page.dart}}».
3. Partager le lien (WhatsApp, SMS). Le parent paie en un geste, le reçu et la mise à jour sont automatiques.
L'élève doit avoir un responsable avec un numéro de téléphone.

## payment_settings
Régler la façon dont l'école facture :
1. Onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{dashboard.payment_settings@presentation/pages/shell/school_money_page.dart}}».
2. Choisir «{{payment.mode_fixed@presentation/pages/payment_settings_page.dart}}» (une mensualité par élève, avec relances automatiques) ou «{{payment.mode_free@presentation/pages/payment_settings_page.dart}}» (chaque famille paie le montant qu'elle veut).
3. En «{{payment.mode_fixed@presentation/pages/payment_settings_page.dart}}» : «{{payment.general_fee_label@presentation/pages/payment_settings_page.dart}}», «{{payment.opening_day_label@presentation/pages/payment_settings_page.dart}}», «{{payment.deadline_day_label@presentation/pages/payment_settings_page.dart}}», et les «{{dashboard.class_fees@presentation/pages/payment_settings_page.dart}}».
4. «{{payment.registration_fee_label@presentation/pages/payment_settings_page.dart}}» : le montant proposé par défaut à chaque nouvel élève.
5. Bouton «{{common.save@presentation/pages/payment_settings_page.dart}}».

## withdraw_money
Retirer l'argent reçu par Wave vers un numéro :
1. Onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{dashboard.payments_overview@presentation/pages/shell/school_money_page.dart}}» → onglet «{{payment.wallet_tab@presentation/pages/school_payments_overview_page.dart}}».
2. Bouton «{{withdraw.title@presentation/pages/school_payments_overview_page.dart}}» ; l'application redemande le mot de passe.
3. «{{withdraw.withdraw_action@presentation/pages/withdrawal/withdrawal_home_page.dart}}», puis les trois étapes «{{withdraw.step_how_much@presentation/pages/withdrawal/withdrawal_flow_page.dart}}», «{{withdraw.step_for_whom@presentation/pages/withdrawal/withdrawal_flow_page.dart}}» («{{withdraw.my_number@presentation/pages/withdrawal/withdrawal_flow_page.dart}}» ou un bénéficiaire), «{{withdraw.step_check@presentation/pages/withdrawal/withdrawal_flow_page.dart}}».
4. Valider. Les frais de retrait s'ajoutent au montant envoyé ; l'écran les montre avant de valider.

## subscription_and_plans
Voir son abonnement, payer un plan tout de suite, ou changer de plan :
1. Onglet «{{nav.money@core/navigation/nav_destinations.dart}}» → «{{subscription.menu_my_sub@presentation/pages/shell/school_money_page.dart}}».
2. Pour payer un plan TOUT DE SUITE (finir l'essai plus tôt, ou monter de plan en cours de mois) : «{{activate.button@presentation/pages/school_subscription_page.dart}}», choisir le plan ; l'écran dit jusqu'à quand le paiement couvre ; puis «{{activate.pay_wallet@presentation/pages/activate_plan_sheet.dart}}» si le solde suffit, sinon payer avec Wave. Le plan s'active dès le paiement. On paie le mois en cours jusqu'au prochain 8, au prix plein, sans prorata ; les jours d'essai restants ne s'ajoutent pas.
3. Pour changer de plan au PROCHAIN prélèvement (notamment pour un plan moins cher) : «{{subscription.change_plan@presentation/pages/school_subscription_page.dart}}», choisir le plan, puis «{{subscription.confirm_change@presentation/pages/school_subscription_page.dart}}».
L'assistant est inclus dans le plan Pro (400 commandes par mois) et illimité dans le plan Grand, dès que le plan est payé. Les prix à jour sont sur idara.sn/plans.

## attendance
Faire l'appel :
1. Onglet «{{nav.today@core/navigation/nav_destinations.dart}}» → «{{dashboard.attendance@presentation/pages/shell/school_today_page.dart}}».
2. Marquer chaque élève, puis enregistrer. L'appel fonctionne même sans connexion internet.

## study_and_grades
Le suivi pédagogique :
- Mémorisation du Coran et journal : onglet «{{nav.today@core/navigation/nav_destinations.dart}}» → «{{dashboard.study_log@presentation/pages/shell/school_today_page.dart}}».
- Notes : onglet «{{nav.today@core/navigation/nav_destinations.dart}}» → «{{dashboard.grades@presentation/pages/shell/school_today_page.dart}}».
- Bulletins PDF : onglet «{{nav.today@core/navigation/nav_destinations.dart}}» → «{{dashboard.report_cards@presentation/pages/shell/school_today_page.dart}}».

## assistant_credits
L'assistant et ses commandes :
1. Onglet «{{nav.home@core/navigation/nav_destinations.dart}}» → carte «{{assistant.title@presentation/widgets/assistant_entry_card.dart}}» → «{{assistant.speak@presentation/widgets/assistant_entry_card.dart}}» (à la voix) ou «{{assistant.write@presentation/widgets/assistant_entry_card.dart}}».
2. Le solde de commandes s'affiche en haut de l'assistant ; bouton «{{assistant.buy@presentation/pages/assistant_page.dart}}» pour en acheter par Wave.
Une commande = une demande traitée. Confirmer une carte est gratuit. Une demande hors sujet n'est pas décomptée.

## account_and_language
- Changer la langue (français / arabe) : le bouton de langue en haut à droite de l'écran.
- Changer son mot de passe : menu ⋮ en haut à droite → «{{changepw.title@presentation/widgets/app_bar_menu.dart}}».
- Signaler un problème à l'équipe Idara : menu ⋮ en haut à droite → «{{report.title@presentation/widgets/app_bar_menu.dart}}».

## school_info
Modifier les informations de l'école (nom en français et en arabe, adresse, téléphone, logo) :
1. Onglet «{{nav.settings@core/navigation/nav_destinations.dart}}» → «{{school_info.title@presentation/pages/shell/school_settings_page.dart}}».
2. Modifier, puis enregistrer.
