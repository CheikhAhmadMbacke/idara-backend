<!-- GÉNÉRÉ par Tools/build-assistant-guide.js depuis guide.template.md — NE PAS MODIFIER À LA MAIN -->
# Guide d'utilisation d'Idara — espace école (direction et personnel)

Les libellés entre « » sont EXACTEMENT ceux affichés à l'écran, dans la langue de
l'application de l'utilisateur. Cite-les tels quels, entre « », même si tu réponds
dans l'autre langue : c'est ce qu'il lit sur son téléphone.
La barre du BAS porte cinq onglets : «Accueil», «Élèves», «Aujourd’hui»,
«Argent», «Réglages». En HAUT à droite : le bouton de langue et le menu ⋮.

## navigation
Les cinq onglets du bas, et ce qu'on y trouve :
1. «Accueil» : les compteurs de l'école, les alertes du jour, et la carte «Assistant IA» (boutons «Parler» et «Écrire»).
2. «Élèves» : la liste des élèves, la recherche «Rechercher un élève», le bouton «Ajouter un élève», et dans le menu ⋮ en haut : «Importer des élèves».
3. «Aujourd’hui» : section «Le pointage du jour» avec «Présences» ; section «Le travail des élèves» avec «Cahier de suivi», «Notes», «Bulletins».
4. «Argent» : section «L’argent qui entre» avec «Paiement» et «Caisse» ; section «Tarifs et abonnement» avec «Tarifs» et «Mon abonnement».
5. «Réglages» : «Parcours de démarrage», section «Classes et programme» («Classes», «Matières», «Affectations enseignants», «Emploi du temps»), section «Les personnes» («Comptes et accès»), et «Mon école».

## add_student
Ajouter un élève à la main :
1. Onglet «Élèves» (barre du bas).
2. Bouton «Ajouter un élève».
3. Remplir au minimum «Prénom» et le nom ; choisir la «Classe».
4. Pour créer le compte du parent : «Ajouter un responsable», puis son nom et «Numéro du responsable» (numéro sénégalais). Le parent reçoit un code de connexion.
5. «Frais d'inscription» : le montant à facturer (vider le champ pour ne rien facturer). «Tarif personnalisé (FCFA / mois)» : seulement si l'élève paie un tarif différent de sa classe.
6. Bouton «Créer» en bas de l'écran.
Raccourci : l'assistant peut aussi préparer l'inscription si on lui dicte l'élève.

## import_students
Importer toute une liste d'élèves d'un coup :
1. Onglet «Élèves».
2. Menu ⋮ en haut à droite → «Importer des élèves».
3. Avec un fichier Excel : «Télécharger le modèle» pour avoir le bon modèle, le remplir, puis «Envoyer mon fichier».
4. Depuis un cahier ou un registre papier : section «Ou partez de votre cahier» → «Ajouter des pages», photographier les pages (ou choisir un PDF).
5. Vérifier l'écran «Ce qui sera créé» (élèves, responsables, problèmes signalés), puis «Créer».
Rien n'est enregistré avant «Créer». Le personnel s'importe de la même façon : onglet «Réglages» → «Comptes et accès» → «Importer le personnel».

## classes_and_fees
Créer une classe et fixer sa mensualité :
1. Onglet «Réglages» → «Classes».
2. Bouton «Nouvelle classe» : «Nom», et «Mensualité (FCFA)» si la classe a son propre tarif.
3. Bouton «Créer».
Le tarif de toutes les classes se voit aussi dans : onglet «Argent» → «Tarifs» → «Tarifs par classe».

## staff_accounts
Créer le compte d'un enseignant ou d'un membre du personnel :
1. Onglet «Réglages» → «Comptes et accès».
2. Bouton «Ajouter un utilisateur» : l'écran «Inviter un utilisateur» s'ouvre.
3. Choisir la «Fonction», saisir le nom et le «Téléphone».
4. Bouton «Envoyer l'invitation» : la personne reçoit ses identifiants par SMS.
Pour affecter un enseignant à une classe : onglet «Réglages» → «Affectations enseignants».

## who_has_not_paid
Voir qui a payé et qui est en retard ce mois-ci :
1. Onglet «Argent» → «Paiement».
2. Onglet «Suivi» : la liste du mois, avec les filtres «En retard», «En attente», «À jour».
Attention : en mode «Montant libre», l'école n'a pas de mensualités ; chaque famille paie ce qu'elle veut, et la liste ne peut pas dire qui « doit ».
Raccourci : demander à l'assistant « Qui n'a pas payé ce mois-ci ? ».

## record_cash_payment
Enregistrer un paiement reçu HORS d'Idara (espèces, ou Wave / Orange Money envoyé directement sur le numéro du daara) :
1. Onglet «Argent» → «Paiement» → onglet «Suivi».
2. Sur la ligne de l'élève, le menu ⋮ → «Encaisser en espèces».
3. Écran «Paiement en espèces» : «Montant reçu (FCFA)», puis «Enregistrer le paiement».
Le reçu part par SMS au parent. Les paiements faits DANS Idara (espace parent ou lien de paiement) s'enregistrent tout seuls : il ne faut pas les ressaisir.

## send_payment_link
Envoyer à un parent son lien de paiement Wave :
1. Onglet «Argent» → «Paiement» → onglet «Suivi».
2. Sur la ligne de l'élève, le menu ⋮ → «Générer un lien de paiement».
3. Partager le lien (WhatsApp, SMS). Le parent paie en un geste, le reçu et la mise à jour sont automatiques.
L'élève doit avoir un responsable avec un numéro de téléphone.

## payment_settings
Régler la façon dont l'école facture :
1. Onglet «Argent» → «Tarifs».
2. Choisir «Montant fixe» (une mensualité par élève, avec relances automatiques) ou «Montant libre» (chaque famille paie le montant qu'elle veut).
3. En «Montant fixe» : «Montant mensuel par élève», «Ouverture du paiement», «Date limite de paiement», et les «Tarifs par classe».
4. «Frais d'inscription» : le montant proposé par défaut à chaque nouvel élève.
5. Bouton «Enregistrer».

## withdraw_money
Retirer l'argent reçu par Wave vers un numéro :
1. Onglet «Argent» → «Paiement» → onglet «Le compte».
2. Bouton «Retrait» ; l'application redemande le mot de passe.
3. «Retirer», puis les trois étapes «Combien ?», «Pour qui ?» («Mon numéro» ou un bénéficiaire), «Vérifier».
4. Valider. Les frais de retrait s'ajoutent au montant envoyé ; l'écran les montre avant de valider.

## subscription_and_plans
Voir son abonnement, payer un plan tout de suite, ou changer de plan :
1. Onglet «Argent» → «Mon abonnement».
2. Pour payer un plan TOUT DE SUITE (finir l'essai plus tôt, ou monter de plan en cours de mois) : «Payer un plan maintenant», choisir le plan ; l'écran dit jusqu'à quand le paiement couvre ; puis «Payer avec mon solde ( )» si le solde suffit, sinon payer avec Wave. Le plan s'active dès le paiement. On paie le mois en cours jusqu'au prochain 8, au prix plein, sans prorata ; les jours d'essai restants ne s'ajoutent pas.
3. Pour changer de plan au PROCHAIN prélèvement (notamment pour un plan moins cher) : «Changer de plan», choisir le plan, puis «Confirmer le changement».
L'assistant est inclus dans le plan Pro (400 commandes par mois) et illimité dans le plan Grand, dès que le plan est payé. Les prix à jour sont sur idara.sn/plans.

## attendance
Faire l'appel :
1. Onglet «Aujourd’hui» → «Présences».
2. Marquer chaque élève, puis enregistrer. L'appel fonctionne même sans connexion internet.

## study_and_grades
Le suivi pédagogique :
- Mémorisation du Coran et journal : onglet «Aujourd’hui» → «Cahier de suivi».
- Notes : onglet «Aujourd’hui» → «Notes».
- Bulletins PDF : onglet «Aujourd’hui» → «Bulletins».

## assistant_credits
L'assistant et ses commandes :
1. Onglet «Accueil» → carte «Assistant IA» → «Parler» (à la voix) ou «Écrire».
2. Le solde de commandes s'affiche en haut de l'assistant ; bouton «Acheter» pour en acheter par Wave.
Une commande = une demande traitée. Confirmer une carte est gratuit. Une demande hors sujet n'est pas décomptée.

## account_and_language
- Changer la langue (français / arabe) : le bouton de langue en haut à droite de l'écran.
- Changer son mot de passe : menu ⋮ en haut à droite → «Changer le mot de passe».
- Signaler un problème à l'équipe Idara : menu ⋮ en haut à droite → «Signaler un problème».

## school_info
Modifier les informations de l'école (nom en français et en arabe, adresse, téléphone, logo) :
1. Onglet «Réglages» → «Mon école».
2. Modifier, puis enregistrer.
