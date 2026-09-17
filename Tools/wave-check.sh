#!/usr/bin/env bash
# =============================================================================
# wave-check.sh — Wave répond-il ? (lecture du solde, aucun mouvement d'argent)
#
#     ssh idara@178.105.202.80
#     bash ~/wave-check.sh
#
# Il lit les secrets déjà posés dans /etc/idara/idara.env, SIGNE la requête
# comme le fait l'application, et interroge le solde.
#
# 🔑 Pourquoi la signature ici : une clé créée avec « signature des requêtes »
# refuse TOUT appel non signé, y compris un simple `curl`. Une vérification qui
# ne signe pas échoue toujours — et fait croire à une mauvaise configuration
# alors que tout est en place. C'est exactement le faux négatif rencontré le
# 2026-09-17.
#
# Ce script ne déplace pas un franc : il lit.
# =============================================================================

set -u

ENV_FILE="/etc/idara/idara.env"

rouge() { printf '\033[31m%s\033[0m\n' "$*"; }
vert()  { printf '\033[32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[33m%s\033[0m\n' "$*"; }
gras()  { printf '\033[1m%s\033[0m\n' "$*"; }

echo
gras "═══ Vérification de l'accès à Wave ═══"
echo

# Distinguer « la cle n'est pas la » de « je n'ai pas pu lire le fichier ».
# Confondre les deux, c'est envoyer chercher un probleme qui n'existe pas --
# la meme faute que la verification qui ne signait pas ses requetes.
if ! sudo -n true 2>/dev/null; then
  if ! sudo -v; then
    rouge "Lecture de $ENV_FILE impossible : sudo refuse."
    exit 1
  fi
fi
CLE=$(sudo grep -m1 '^Wave__ApiKey=' "$ENV_FILE" 2>/dev/null | cut -d= -f2-)
SIGNING=$(sudo grep -m1 '^Wave__SigningSecret=' "$ENV_FILE" 2>/dev/null | cut -d= -f2-)

if [ -z "${CLE:-}" ]; then
  rouge "Wave__ApiKey absent de $ENV_FILE — lance d'abord wave-setup-secrets.sh"
  exit 1
fi
echo "Clé      : ${#CLE} caractères, se termine par …${CLE: -4}"

EN_TETES=(-H "Authorization: Bearer $CLE")

if [ -n "${SIGNING:-}" ]; then
  echo "Signature: ${#SIGNING} caractères, se termine par …${SIGNING: -4}"
  # Pour un GET sans corps, la charge signée est l'horodatage SEUL.
  TS=$(date +%s)
  SIG=$(printf '%s' "$TS" | openssl dgst -sha256 -hmac "$SIGNING" | sed 's/^.* //')
  EN_TETES+=(-H "Wave-Signature: t=${TS},v1=${SIG}")
  echo "         → requête signée (t=$TS)"
else
  jaune "Aucun secret de signature : appel NON signé."
  jaune "Si la clé a été créée avec « signature des requêtes », Wave refusera."
fi

echo
REP=$(curl -s -m 20 -o /tmp/wave_chk.$$ -w '%{http_code}' \
  "${EN_TETES[@]}" https://api.wave.com/v1/balance 2>/dev/null)
CORPS=$(cat /tmp/wave_chk.$$ 2>/dev/null); rm -f /tmp/wave_chk.$$

case "$REP" in
  200)
    vert "✓ Wave répond. Solde du compte marchand : $CORPS"
    echo
    gras "La clé, la signature et l'adresse IP sont toutes bonnes."
    echo "Étape suivante, depuis TON POSTE (node n'est pas installé ici) :"
    echo "   node Idara.API/Tools/wave-webhook-selftest.js \\"
    echo "        https://api.idara.sn/api/webhooks/wave <secret-webhook>"
    ;;
  403)
    rouge "✗ 403 — $CORPS"
    echo
    jaune "Si le code est « ip-not-allowed » : ajoute 178.105.202.80/32 dans"
    jaune "Développeurs → Liste blanche IP. Wave l'active automatiquement sur"
    jaune "la première clé, et rien ne passe tant que l'adresse n'y est pas."
    ;;
  401)
    rouge "✗ 401 — $CORPS"
    echo
    case "$CORPS" in
      *missing-signature*)
        jaune "La clé exige une signature et aucune n'a été envoyée : il manque"
        jaune "Wave__SigningSecret. Relance wave-setup-secrets.sh." ;;
      *invalid-signature-timestamp*|*expired-signature*)
        jaune "Horodatage refusé : l'horloge du serveur a dérivé. Vérifie timedatectl." ;;
      *invalid-signature*)
        jaune "Signature calculée ≠ signature attendue : le secret de signature"
        jaune "ne correspond pas à cette clé. Ils vont par PAIRE — un secret"
        jaune "d'une ancienne clé ne marchera jamais avec une clé neuve." ;;
      *no-matching-api-key*|*api-key-revoked*)
        jaune "Cette clé n'existe pas ou a été révoquée côté Wave." ;;
      *) jaune "Voir le code d'erreur ci-dessus." ;;
    esac
    ;;
  000|"")
    rouge "✗ Aucune réponse : réseau ou délai dépassé. La clé n'est pas en cause."
    ;;
  *)
    rouge "✗ HTTP $REP — $CORPS"
    ;;
esac
echo
