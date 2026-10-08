#!/usr/bin/env bash
# =============================================================================
# assistant-setup-secrets.sh — poser la clé Anthropic DÉDIÉE à l'assistant IA.
#
# Pourquoi une clé à part (2026-10-08) : tant que l'assistant partageait la clé
# de la lecture de cahier, la console Anthropic additionnait les deux, et le
# coût réel de l'assistant ne pouvait pas se lire. Dans son propre ESPACE DE
# TRAVAIL (workspace), il a son coût exact — et son propre plafond de dépense.
#
# À lancer SUR LE SERVEUR (le déploiement ne copie pas Tools/) :
#     scp Idara.API/Tools/assistant-setup-secrets.sh Idara.API/Tools/assistant-check.sh idara@178.105.202.80:~/
#     ssh -t idara@178.105.202.80 "bash ~/assistant-setup-secrets.sh"
#
# Le `-t` est indispensable : sans pseudo-terminal, la saisie invisible échoue.
#
# Ce que fait le script, dans l'ordre (règle du projet, CLAUDE.md « Secrets ») :
#   1. sauvegarde horodatée de /etc/idara/idara.env ;
#   2. saisie INVISIBLE, espaces du copier-coller retirés, préfixe vérifié ;
#   3. confirmation en RECOLLANT ;
#   4. 🔑 appel RÉEL à Anthropic AVANT d'écrire (count_tokens : gratuit, même
#      authentification que l'application) — une clé refusée n'est jamais posée ;
#   5. remplacement de la seule ligne Assistant__ApiKey, chmod 600 ;
#   6. redémarrage proposé, puis vérification que le service est reparti.
#
# La clé ne passe JAMAIS en argument de commande : elle irait dans `ps` et dans
# l'historique (§190). Elle transite par un fichier d'en-têtes temporaire (600).
# =============================================================================
set -u
ENV_FILE="/etc/idara/idara.env"
SERVICE="idara-api"
VAR="Assistant__ApiKey"
PREFIXE="sk-ant-api"
MODELE="claude-sonnet-5-5"

rouge()  { printf '\033[31m%s\033[0m\n' "$*"; }
vert()   { printf '\033[32m%s\033[0m\n' "$*"; }
jaune()  { printf '\033[33m%s\033[0m\n' "$*"; }
gras()   { printf '\033[1m%s\033[0m\n' "$*"; }

# Appel réel : rend le code HTTP. La clé est lue dans un fichier d'en-têtes.
verifier_cle() {
  local cle="$1" entetes code
  entetes=$(mktemp); chmod 600 "$entetes"
  printf 'x-api-key: %s\nanthropic-version: 2023-06-01\ncontent-type: application/json\n' "$cle" > "$entetes"
  code=$(curl -s -o /tmp/assistant-check.$$ -w '%{http_code}' --max-time 20 \
    -H @"$entetes" \
    -d "{\"model\":\"$MODELE\",\"messages\":[{\"role\":\"user\",\"content\":\"Salam\"}]}" \
    https://api.anthropic.com/v1/messages/count_tokens)
  rm -f "$entetes"
  echo "$code"
}

echo
gras "═══ Clé Anthropic de l'assistant IA ═══"
echo
if [ ! -f "$ENV_FILE" ]; then
  rouge "Fichier introuvable : $ENV_FILE — es-tu bien sur le serveur de production ?"
  exit 1
fi

# sudo d'abord : sans lui, la lecture du fichier échoue EN SILENCE et le
# script conclurait à tort qu'aucune clé n'est posée.
sudo -v || { rouge "sudo refusé : relance avec  ssh -t idara@178.105.202.80 \"bash ~/$(basename "$0")\""; exit 1; }
BACKUP="${ENV_FILE}.$(date -u +%Y%m%d-%H%M%S).bak"
sudo cp -p "$ENV_FILE" "$BACKUP" || { rouge "Sauvegarde impossible — on s'arrête."; exit 1; }
vert "Sauvegarde : $BACKUP"

actuelle=$(sudo grep -m1 "^${VAR}=" "$ENV_FILE" 2>/dev/null | cut -d= -f2-)
if [ -n "${actuelle:-}" ]; then
  jaune "Une clé dédiée est déjà en place (se termine par …${actuelle: -4})."
  printf 'La remplacer ? [o/N] '
  read -r reponse </dev/tty
  case "$reponse" in [oO]|[oO][uU][iI]) ;; *) echo "Inchangée."; exit 0 ;; esac
else
  jaune "Aucune clé dédiée : l'assistant utilise aujourd'hui celle de la lecture de cahier (Vision__ApiKey)."
fi

echo
echo "Format attendu : ${PREFIXE}…  (console.anthropic.com → espace de travail de l'assistant → API keys)"
while true; do
  printf 'Clé (la saisie reste invisible) : '
  read -rs cle </dev/tty; echo
  cle="$(printf '%s' "$cle" | tr -d '[:space:]')"
  if [ -z "$cle" ]; then rouge "Vide. Recommence."; continue; fi
  if [ "${cle#"$PREFIXE"}" = "$cle" ]; then
    rouge "Cette valeur ne commence pas par « $PREFIXE » — ce n'est pas une clé d'API Anthropic."
    continue
  fi
  if [ ${#cle} -lt 40 ]; then
    rouge "Seulement ${#cle} caractères : une clé tronquée, sans doute. Recommence."
    continue
  fi
  printf 'Confirme en la recollant : '
  read -rs confirmation </dev/tty; echo
  confirmation="$(printf '%s' "$confirmation" | tr -d '[:space:]')"
  if [ "$cle" != "$confirmation" ]; then rouge "Les deux saisies diffèrent. On recommence."; continue; fi
  break
done

echo
gras "── Vérification auprès d'Anthropic (count_tokens : gratuit, aucune dépense)"
code=$(verifier_cle "$cle")
case "$code" in
  200) vert "   ✓ Anthropic accepte la clé (HTTP 200)." ;;
  401) rouge "   ✗ Clé refusée (HTTP 401) : invalide ou révoquée. RIEN n'a été écrit."; rm -f /tmp/assistant-check.$$; exit 1 ;;
  403) rouge "   ✗ Accès interdit (HTTP 403) : la clé n'a pas le droit d'utiliser $MODELE. RIEN n'a été écrit."; rm -f /tmp/assistant-check.$$; exit 1 ;;
  *)   rouge "   ✗ Réponse inattendue (HTTP $code). RIEN n'a été écrit."; head -c 300 /tmp/assistant-check.$$; echo; rm -f /tmp/assistant-check.$$; exit 1 ;;
esac
rm -f /tmp/assistant-check.$$

tmp=$(mktemp)
sudo grep -v "^${VAR}=" "$ENV_FILE" > "$tmp" 2>/dev/null || true
printf '%s=%s\n' "$VAR" "$cle" >> "$tmp"
sudo cp "$tmp" "$ENV_FILE"
rm -f "$tmp"
sudo chown idara:idara "$ENV_FILE"
sudo chmod 600 "$ENV_FILE"
vert "✓ $VAR enregistrée (${#cle} caractères, se termine par …${cle: -4}), droits 600."

echo
printf 'Redémarrer %s maintenant ? [O/n] ' "$SERVICE"
read -r redemarrer </dev/tty
case "${redemarrer:-o}" in
  [nN]|[nN][oO][nN]) jaune "Non redémarré — la clé sera lue au prochain démarrage."; exit 0 ;;
esac
sudo systemctl restart "$SERVICE"
sleep 5
if ! systemctl is-active --quiet "$SERVICE"; then
  rouge "Le service N'EST PAS reparti. Les 30 dernières lignes :"
  sudo journalctl -u "$SERVICE" -n 30 --no-pager
  jaune "Pour revenir en arrière : sudo cp $BACKUP $ENV_FILE && sudo systemctl restart $SERVICE"
  exit 1
fi
vert "Service actif. L'assistant utilise désormais sa clé dédiée."
echo
echo "Pour revérifier plus tard, sans rien ressaisir : ssh -t idara@178.105.202.80 \"bash ~/assistant-check.sh\""
