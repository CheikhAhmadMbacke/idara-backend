#!/usr/bin/env bash
# =============================================================================
# email-setup-secrets.sh — basculer l'envoi des courriels de Gmail vers la
# messagerie professionnelle Namecheap Private Email, un champ à la fois.
#
# À lancer SUR LE SERVEUR (le déploiement ne copie pas Tools/ ; le script est
# posé dans le dossier personnel) :
#     scp Idara.API/Tools/email-setup-secrets.sh idara@178.105.202.80:~/
#     ssh idara@178.105.202.80
#     bash ~/email-setup-secrets.sh
#
# Pourquoi un script plutôt qu'un `nano` : le mot de passe d'application ne
# doit apparaître ni à l'écran, ni dans l'historique du shell, ni dans un
# argument de ligne de commande (§190). Et surtout, la bascule ne se limite
# pas au mot de passe : l'identifiant de session DIFFÈRE désormais de
# l'expéditeur — on ouvre la session avec contact@pyranil.com et on expédie
# depuis l'alias idara@pyranil.com. Se tromper d'un des deux donne un envoi
# qui part… mais depuis la mauvaise adresse, ce qui ne se voit qu'à la
# réception.
#
# Ce que le script NE fait PAS, volontairement :
#   - il n'affiche jamais le mot de passe ;
#   - il ne le laisse pas dans l'historique (saisie, pas argument) ;
#   - il ne touche à aucune autre ligne du fichier.
#
# Et ce qu'il fait, lui : un ENVOI RÉEL, en STARTTLS sur le port 587, avec
# AUTH — exactement le protocole que parle SmtpClient dans EmailService.cs.
# Un contrôle qui se contenterait d'ouvrir le port accuserait le mot de passe
# qu'il vient d'écrire (faux négatif rencontré sur Wave le 2026-09-17).
# =============================================================================

set -u

ENV_FILE="/etc/idara/idara.env"
SERVICE="idara-api"

rouge()  { printf '\033[31m%s\033[0m\n' "$*"; }
vert()   { printf '\033[32m%s\033[0m\n' "$*"; }
jaune()  { printf '\033[33m%s\033[0m\n' "$*"; }
gras()   { printf '\033[1m%s\033[0m\n' "$*"; }

echo
gras "=== Bascule des courriels vers Namecheap Private Email ==="
echo

if [ ! -f "$ENV_FILE" ]; then
  rouge "Fichier introuvable : $ENV_FILE"
  rouge "Es-tu bien sur le serveur de production ?"
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  rouge "python3 est absent : la vérification d'envoi réel serait impossible."
  exit 1
fi

# --- Sauvegarde horodatée AVANT toute écriture -------------------------------
# Le fichier porte tous les secrets de la plateforme : SMS, base, Wave.
# Une copie coûte un octet et évite une soirée de récupération.
BACKUP="${ENV_FILE}.$(date -u +%Y%m%d-%H%M%S).bak"
sudo cp -p "$ENV_FILE" "$BACKUP" || { rouge "Sauvegarde impossible — on s'arrête."; exit 1; }
vert "Sauvegarde : $BACKUP"

# --- Écriture d'une ligne, sans toucher au reste -----------------------------
ecrire() {
  local var="$1" valeur="$2" tmp
  tmp=$(mktemp)
  sudo grep -v "^${var}=" "$ENV_FILE" > "$tmp" 2>/dev/null || true
  printf '%s=%s\n' "$var" "$valeur" >> "$tmp"
  sudo cp "$tmp" "$ENV_FILE"
  rm -f "$tmp"
}

lire() { sudo grep -m1 "^${1}=" "$ENV_FILE" 2>/dev/null | cut -d= -f2-; }

# --- Valeurs NON secrètes : proposées, confirmables --------------------------
# Elles n'ont pas à être tapées en aveugle — les cacher ne protégerait rien et
# empêcherait de relire ce qu'on pose.
poser_visible() {
  local var="$1" libelle="$2" defaut="$3" actuelle saisie
  actuelle=$(lire "$var")
  echo
  gras "-- $libelle"
  if [ -n "${actuelle:-}" ]; then echo "   Actuellement : $actuelle"; fi
  printf '   Valeur [%s] : ' "$defaut"
  read -r saisie </dev/tty
  saisie="$(printf '%s' "${saisie:-$defaut}" | tr -d '[:space:]')"
  ecrire "$var" "$saisie"
  vert "   OK  $var = $saisie"
}

poser_visible "EmailSettings__SmtpServer"   "Serveur SMTP"                      "mail.privateemail.com"
# 587 et pas 465 : SmtpClient (.NET) ne sait faire que du STARTTLS. Sur le
# port 465, SSL implicite, il reste bloqué jusqu'au timeout — sans message.
poser_visible "EmailSettings__SmtpPort"     "Port (587 = STARTTLS, NE PAS mettre 465)" "587"
poser_visible "EmailSettings__SmtpUsername" "Identifiant de session (la BOITE, pas l'alias)" "contact@pyranil.com"
poser_visible "EmailSettings__SenderEmail"  "Expéditeur affiché (l'ALIAS)"      "idara@pyranil.com"
poser_visible "EmailSettings__ReplyTo"      "Adresse de réponse"                "idara@pyranil.com"
poser_visible "EmailSettings__SenderName"   "Nom affiché"                       "Idara"

# --- Le mot de passe d'application -------------------------------------------
echo
gras "-- Mot de passe d'application « Idara » (saisie invisible)"
ACTUEL=$(lire "EmailSettings__SenderPassword")
MDP=""
if [ -n "${ACTUEL:-}" ]; then
  jaune "   Un mot de passe est déjà en place (se termine par …${ACTUEL: -3})."
  printf '   Le remplacer ? [O/n] '
  read -r reponse </dev/tty
  case "${reponse:-o}" in
    [nN]|[nN][oO][nN]) MDP="$ACTUEL"; jaune "   Inchangé." ;;
  esac
fi

while [ -z "${MDP:-}" ]; do
  printf '   Mot de passe (invisible) : '
  read -rs valeur </dev/tty; echo
  # Un copier-coller ramène souvent une espace ou un retour à la ligne :
  # invisibles, et ils cassent l'authentification.
  valeur="$(printf '%s' "$valeur" | tr -d '[:space:]')"

  if [ -z "$valeur" ]; then rouge "   Obligatoire. Recommence."; continue; fi
  if [ ${#valeur} -lt 8 ]; then
    rouge "   Seulement ${#valeur} caractères : une valeur tronquée, sans doute."
    printf '   La garder quand même ? [o/N] '
    read -r forcer </dev/tty
    case "$forcer" in [oO]|[oO][uU][iI]) ;; *) continue ;; esac
  fi

  printf '   Confirme en le recollant : '
  read -rs confirmation </dev/tty; echo
  confirmation="$(printf '%s' "$confirmation" | tr -d '[:space:]')"
  if [ "$valeur" != "$confirmation" ]; then
    rouge "   Les deux saisies diffèrent. On recommence."
    continue
  fi
  MDP="$valeur"
done

ecrire "EmailSettings__SenderPassword" "$MDP"
vert "   OK  Mot de passe enregistré (${#MDP} caractères)"

# --- Droits ------------------------------------------------------------------
sudo chown idara:idara "$ENV_FILE"
sudo chmod 600 "$ENV_FILE"
echo
vert "Droits remis à 600, propriétaire idara."

# --- Vérification RÉELLE, avant même de redémarrer ---------------------------
# On envoie pour de bon, dans le protocole exact de l'application. Si ça ne
# part pas, inutile de redémarrer le service avec une configuration morte.
echo
gras "-- Vérification : envoi réel, STARTTLS + AUTH sur le port 587"
printf "   Destinataire du message d'essai : "
read -r DEST </dev/tty
DEST="$(printf '%s' "$DEST" | tr -d '[:space:]')"

if [ -z "$DEST" ]; then
  jaune "   Aucun destinataire — vérification sautée. La configuration n'est PAS prouvée."
else
  # Le mot de passe passe par l'entrée standard, jamais par un argument
  # (visible dans `ps`) ni par l'environnement.
  if ! printf '%s' "$MDP" | python3 "$HOME/email-selftest.py" \
      "$(lire EmailSettings__SmtpServer)" \
      "$(lire EmailSettings__SmtpPort)" \
      "$(lire EmailSettings__SmtpUsername)" \
      "$(lire EmailSettings__SenderEmail)" \
      "$(lire EmailSettings__SenderName)" \
      "$(lire EmailSettings__ReplyTo)" \
      "$DEST"; then
    echo
    rouge "L'envoi a ÉCHOUÉ. La configuration n'est pas bonne — le service n'a PAS été redémarré."
    jaune "Pour revenir en arrière : sudo cp $BACKUP $ENV_FILE"
    exit 1
  fi
  vert "   OK  Message parti. Va le lire : il doit venir de « Idara <…> », hors indésirables."
fi

# --- Redémarrage -------------------------------------------------------------
echo
printf 'Redémarrer %s maintenant ? [O/n] ' "$SERVICE"
read -r redemarrer </dev/tty
case "${redemarrer:-o}" in
  [nN]|[nN][oO][nN])
    jaune "Non redémarré — les nouvelles valeurs ne seront lues qu'au prochain démarrage."
    exit 0
    ;;
esac

sudo systemctl restart "$SERVICE"
sleep 4

if ! systemctl is-active --quiet "$SERVICE"; then
  rouge "Le service N'EST PAS reparti. Les 30 dernières lignes :"
  sudo journalctl -u "$SERVICE" -n 30 --no-pager
  echo
  jaune "Pour revenir en arrière : sudo cp $BACKUP $ENV_FILE && sudo systemctl restart $SERVICE"
  exit 1
fi
vert "Service actif."
echo
gras "Terminé. Pour re-vérifier plus tard sans rien ressaisir : bash ~/email-check.sh"
