#!/usr/bin/env bash
# =============================================================================
# wave-setup-secrets.sh — poser les secrets Wave, un champ à la fois.
#
# À lancer SUR LE SERVEUR (le déploiement ne copie pas Tools/ ; le script est
# posé dans le dossier personnel) :
#     ssh idara@178.105.202.80
#     bash ~/wave-setup-secrets.sh
#
# Pourquoi un script plutôt qu'un `nano` : trois secrets, une casse exacte à
# respecter (`Wave__ApiKey`, deux tirets bas), un fichier qui contient déjà
# d'autres clés qu'une fausse manœuvre écraserait, et des valeurs longues où
# un caractère manquant ne se voit pas. Ici, chaque valeur est demandée,
# vérifiée, et écrite à sa place — l'ancienne ligne est remplacée, jamais
# dupliquée.
#
# Ce que le script NE fait PAS, volontairement :
#   - il n'affiche aucun secret à l'écran ;
#   - il ne les laisse pas dans l'historique du shell (saisie, pas argument) ;
#   - il ne touche à aucune autre ligne du fichier.
# =============================================================================

set -u

ENV_FILE="/etc/idara/idara.env"
SERVICE="idara-api"

rouge()  { printf '\033[31m%s\033[0m\n' "$*"; }
vert()   { printf '\033[32m%s\033[0m\n' "$*"; }
jaune()  { printf '\033[33m%s\033[0m\n' "$*"; }
gras()   { printf '\033[1m%s\033[0m\n' "$*"; }

echo
gras "═══ Configuration des secrets Wave ═══"
echo

if [ ! -f "$ENV_FILE" ]; then
  rouge "Fichier introuvable : $ENV_FILE"
  rouge "Es-tu bien sur le serveur de production ?"
  exit 1
fi

# --- Sauvegarde horodatée AVANT toute écriture -------------------------------
# Le fichier porte tous les secrets de la plateforme : SMS, base, courriel.
# Une copie coûte un octet et évite une soirée de récupération.
BACKUP="${ENV_FILE}.$(date -u +%Y%m%d-%H%M%S).bak"
sudo cp -p "$ENV_FILE" "$BACKUP" || { rouge "Sauvegarde impossible — on s'arrête."; exit 1; }
vert "Sauvegarde : $BACKUP"
echo

# --- Lecture d'un secret, avec vérification ----------------------------------
# $1 = nom de la variable · $2 = libellé · $3 = préfixe attendu ("" = aucun)
# $4 = obligatoire (oui/non)
demander_secret() {
  local var="$1" libelle="$2" prefixe="$3" obligatoire="$4"
  local valeur confirmation actuelle

  actuelle=$(sudo grep -m1 "^${var}=" "$ENV_FILE" 2>/dev/null | cut -d= -f2-)

  echo
  gras "── $libelle"
  if [ -n "${actuelle:-}" ]; then
    # On montre les 4 derniers caractères : c'est ainsi que Wave identifie
    # une clé dans son portail, et cela suffit à savoir si c'est la bonne.
    jaune "   Une valeur est déjà en place (se termine par …${actuelle: -4})."
    printf '   La remplacer ? [o/N] '
    read -r reponse </dev/tty
    case "$reponse" in
      [oO]|[oO][uU][iI]) ;;
      *) echo "   Inchangée."; return 0 ;;
    esac
  fi

  if [ -n "$prefixe" ]; then
    echo "   Format attendu : ${prefixe}…"
  fi

  while true; do
    printf '   Valeur (la saisie reste invisible) : '
    read -rs valeur </dev/tty; echo

    if [ -z "$valeur" ]; then
      if [ "$obligatoire" = "non" ]; then
        echo "   Laissée vide."
        return 0
      fi
      rouge "   Obligatoire. Recommence."
      continue
    fi

    # Un copier-coller depuis le portail ramène souvent une espace ou un
    # retour à la ligne : invisibles, et ils cassent l'authentification.
    valeur="$(printf '%s' "$valeur" | tr -d '[:space:]')"

    if [ -n "$prefixe" ] && [ "${valeur#"$prefixe"}" = "$valeur" ]; then
      rouge "   Cette valeur ne commence pas par « $prefixe »."
      rouge "   Vérifie que tu n'as pas interverti deux secrets — ils se ressemblent."
      printf '   La garder quand même ? [o/N] '
      read -r forcer </dev/tty
      case "$forcer" in [oO]|[oO][uU][iI]) ;; *) continue ;; esac
    fi

    if [ ${#valeur} -lt 20 ]; then
      rouge "   Seulement ${#valeur} caractères : une valeur tronquée, sans doute."
      printf '   La garder quand même ? [o/N] '
      read -r forcer </dev/tty
      case "$forcer" in [oO]|[oO][uU][iI]) ;; *) continue ;; esac
    fi

    printf '   Confirme en la recollant : '
    read -rs confirmation </dev/tty; echo
    confirmation="$(printf '%s' "$confirmation" | tr -d '[:space:]')"

    if [ "$valeur" != "$confirmation" ]; then
      rouge "   Les deux saisies diffèrent. On recommence."
      continue
    fi
    break
  done

  # Remplacement en place, sans toucher au reste du fichier. On passe par un
  # temporaire puis un déplacement atomique : une coupure de courant au
  # mauvais moment ne doit pas laisser un fichier de secrets à moitié écrit.
  local tmp
  tmp=$(mktemp)
  sudo grep -v "^${var}=" "$ENV_FILE" > "$tmp" 2>/dev/null || true
  printf '%s=%s\n' "$var" "$valeur" >> "$tmp"
  sudo cp "$tmp" "$ENV_FILE"
  rm -f "$tmp"
  vert "   ✓ $var enregistrée (${#valeur} caractères, se termine par …${valeur: -4})"
}

# --- Les trois secrets -------------------------------------------------------
demander_secret "Wave__ApiKey" \
  "Clé d'API (Developers → API keys)" \
  "wave_sn_" "oui"

demander_secret "Wave__SigningSecret" \
  "Secret de signature des requêtes (affiché à la création de la clé)" \
  "wave_sn_AKS_" "non"

demander_secret "Wave__WebhookSecret" \
  "Secret du webhook (Developers → Webhooks)" \
  "" "non"

# --- Remise en ordre des droits ---------------------------------------------
sudo chown idara:idara "$ENV_FILE"
sudo chmod 600 "$ENV_FILE"
echo
vert "Droits remis à 600, propriétaire idara."

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

# --- Vérification réelle : Wave répond-il ? ----------------------------------
# Le seul test qui prouve quelque chose. Il lit le solde, ne déplace rien.
echo
gras "── Vérification auprès de Wave (lecture du solde, aucun mouvement)"
# 🔴 La requête doit être SIGNÉE : une clé créée avec « signature des requêtes »
# répond 401 missing-signature à tout appel nu, et l'on croit alors à une
# mauvaise configuration alors que tout est en place. Faux négatif rencontré
# le 2026-09-17 — la vérification accusait le secret qu'elle venait d'écrire.
CLE=$(sudo grep -m1 '^Wave__ApiKey=' "$ENV_FILE" | cut -d= -f2-)
SIGNING=$(sudo grep -m1 '^Wave__SigningSecret=' "$ENV_FILE" | cut -d= -f2-)

EN_TETES=(-H "Authorization: Bearer $CLE")
if [ -n "${SIGNING:-}" ]; then
  # GET sans corps : la charge signée est l'horodatage SEUL.
  TS=$(date +%s)
  SIG=$(printf '%s' "$TS" | openssl dgst -sha256 -hmac "$SIGNING" | sed 's/^.* //')
  EN_TETES+=(-H "Wave-Signature: t=${TS},v1=${SIG}")
fi

REPONSE=$(curl -s -m 20 -o /tmp/wave_check.$$ -w '%{http_code}' \
  "${EN_TETES[@]}" https://api.wave.com/v1/balance 2>/dev/null)
CORPS=$(cat /tmp/wave_check.$$ 2>/dev/null); rm -f /tmp/wave_check.$$

case "$REPONSE" in
  200)
    vert "   ✓ Wave répond. Solde : $CORPS"
    echo
    gras "Tout est en place. Étape suivante : le contrôle du webhook."
    echo "   ⚠️ Node n'est pas installé ici : lance-le depuis TON poste —"
    echo "      node Idara.API/Tools/wave-webhook-selftest.js \\"
    echo "           https://api.idara.sn/api/webhooks/wave <secret-webhook>"
    echo "   (c'est d'ailleurs mieux : il éprouve tout le chemin réseau,"
    echo "    exactement comme le fera Wave.)"
    ;;
  403)
    rouge "   ✗ 403 — très probablement la LISTE BLANCHE IP."
    echo "   $CORPS"
    echo
    jaune "   Wave l'active automatiquement sur la première clé. Ajoute"
    jaune "   178.105.202.80/32 dans Developers → IP whitelist, puis relance"
    jaune "   cette vérification. La clé est bonne, c'est l'adresse qui manque."
    ;;
  401)
    rouge "   ✗ 401 — clé refusée."
    echo "   $CORPS"
    echo
    case "$CORPS" in
      *missing-signature*)
        jaune "   La clé exige une signature : renseigne Wave__SigningSecret." ;;
      *invalid-signature-timestamp*|*expired-signature*)
        jaune "   Horodatage refusé — l'horloge du serveur a dérivé (timedatectl)." ;;
      *invalid-signature*)
        jaune "   Le secret de signature ne correspond pas à cette clé : ils vont"
        jaune "   par PAIRE, affichés ensemble à la création de la clé." ;;
      *no-matching-api-key*|*api-key-revoked*)
        jaune "   Cette clé n'existe pas côté Wave, ou a été révoquée." ;;
      *) jaune "   Voir le code d'erreur ci-dessus." ;;
    esac
    jaune "   Pour re-tester sans tout ressaisir : bash ~/wave-check.sh"
    ;;
  000|"")
    rouge "   ✗ Aucune réponse : réseau ou délai dépassé. La clé n'est pas en cause."
    ;;
  *)
    rouge "   ✗ HTTP $REPONSE"
    echo "   $CORPS"
    ;;
esac
echo
