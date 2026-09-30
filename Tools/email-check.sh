#!/usr/bin/env bash
# =============================================================================
# email-check.sh — re-vérifier la messagerie SANS rien ressaisir.
#
#     ssh idara@178.105.202.80
#     bash ~/email-check.sh                  # envoie a l'adresse demandee
#
# Pendant de email-setup-secrets.sh : celui-ci POSE la configuration, celui-là
# se contente de la RELIRE et de prouver qu'elle marche encore. On ne veut pas
# avoir à retaper un mot de passe pour répondre à « est-ce que ça marche
# toujours ? » — sinon on ne pose jamais la question.
#
# Il n'écrit rien, ne redémarre rien, et n'affiche aucun secret.
# =============================================================================

set -u

ENV_FILE="/etc/idara/idara.env"

rouge()  { printf '\033[31m%s\033[0m\n' "$*"; }
vert()   { printf '\033[32m%s\033[0m\n' "$*"; }
jaune()  { printf '\033[33m%s\033[0m\n' "$*"; }
gras()   { printf '\033[1m%s\033[0m\n' "$*"; }

lire() { sudo grep -m1 "^${1}=" "$ENV_FILE" 2>/dev/null | cut -d= -f2-; }

echo
gras "=== Vérification de la messagerie Idara ==="

if [ ! -f "$ENV_FILE" ]; then
  rouge "Fichier introuvable : $ENV_FILE"
  exit 1
fi

HOTE=$(lire EmailSettings__SmtpServer)
PORT=$(lire EmailSettings__SmtpPort)
IDENT=$(lire EmailSettings__SmtpUsername)
EXPED=$(lire EmailSettings__SenderEmail)
NOM=$(lire EmailSettings__SenderName)
REPLY=$(lire EmailSettings__ReplyTo)
MDP=$(lire EmailSettings__SenderPassword)

# Repli exactement identique à celui du code (EmailSettings.EffectiveUsername
# et EffectiveReplyTo) : le contrôle doit se tromper là où l'application se
# tromperait, sinon il ne prouve rien.
if [ -z "${IDENT:-}" ]; then IDENT="$EXPED"; fi
if [ -z "${REPLY:-}" ]; then REPLY="$EXPED"; fi
if [ -z "${NOM:-}" ];   then NOM="Idara"; fi
if [ -z "${PORT:-}" ];  then PORT="587"; fi

echo
echo "   Serveur              : ${HOTE:-(absent)}:${PORT}"
echo "   Session ouverte avec : ${IDENT:-(absent)}"
echo "   Expéditeur affiché   : ${EXPED:-(absent)}"
echo "   Répondre à           : ${REPLY}"
echo "   Nom affiché          : ${NOM}"
if [ -n "${MDP:-}" ]; then
  echo "   Mot de passe         : présent (${#MDP} caractères)"
else
  rouge "   Mot de passe         : ABSENT"
fi

if [ -z "${HOTE:-}" ] || [ -z "${EXPED:-}" ] || [ -z "${MDP:-}" ]; then
  echo
  rouge "Configuration incomplète — lancer email-setup-secrets.sh."
  exit 1
fi

case "$HOTE" in
  *gmail*)
    echo
    jaune "Attention : le serveur est encore Gmail. La bascule vers Private"
    jaune "Email n'a pas été faite (ou a été annulée)."
    ;;
esac

echo
printf "   Destinataire du message d'essai (vide = ne rien envoyer) : "
read -r DEST </dev/tty
DEST="$(printf '%s' "$DEST" | tr -d '[:space:]')"

if [ -z "$DEST" ]; then
  jaune "Aucun envoi. La configuration est lisible, mais elle n'est pas PROUVÉE."
  exit 0
fi

if ! printf '%s' "$MDP" | python3 "$HOME/email-selftest.py" \
    "$HOTE" "$PORT" "$IDENT" "$EXPED" "$NOM" "$REPLY" "$DEST"; then
  echo
  rouge "La messagerie NE marche PAS."
  exit 1
fi

echo
vert "La messagerie marche. Va lire le message : expéditeur, dossier, en-têtes."
