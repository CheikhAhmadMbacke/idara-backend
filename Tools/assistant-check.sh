#!/usr/bin/env bash
# =============================================================================
# assistant-check.sh — revérifier la clé Anthropic de l'assistant SANS la
# ressaisir. Pendant d'assistant-setup-secrets.sh.
#
#     ssh -t idara@178.105.202.80 "bash ~/assistant-check.sh"
#
# Appel GRATUIT (count_tokens), même authentification que l'application.
# =============================================================================
set -u
ENV_FILE="/etc/idara/idara.env"
rouge() { printf '\033[31m%s\033[0m\n' "$*"; }
vert()  { printf '\033[32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[33m%s\033[0m\n' "$*"; }

# sudo d'abord : sans lui, la lecture du fichier échoue EN SILENCE et le
# script conclurait à tort qu'aucune clé n'est posée.
sudo -v || { rouge "sudo refusé : relance avec  ssh -t idara@178.105.202.80 \"bash ~/$(basename "$0")\""; exit 1; }
cle=$(sudo grep -m1 '^Assistant__ApiKey=' "$ENV_FILE" | cut -d= -f2-)
source_cle="Assistant__ApiKey (clé dédiée)"
if [ -z "${cle:-}" ]; then
  cle=$(sudo grep -m1 '^Vision__ApiKey=' "$ENV_FILE" | cut -d= -f2-)
  source_cle="Vision__ApiKey (clé PARTAGÉE avec la lecture de cahier — pas de clé dédiée)"
fi
if [ -z "${cle:-}" ]; then rouge "Aucune clé Anthropic configurée : l'assistant est éteint."; exit 1; fi
jaune "Clé utilisée : $source_cle, se termine par …${cle: -4}"

entetes=$(mktemp); chmod 600 "$entetes"
printf 'x-api-key: %s\nanthropic-version: 2023-06-01\ncontent-type: application/json\n' "$cle" > "$entetes"
code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 -H @"$entetes" \
  -d '{"model":"claude-sonnet-5-5","messages":[{"role":"user","content":"Salam"}]}' \
  https://api.anthropic.com/v1/messages/count_tokens)
rm -f "$entetes"
if [ "$code" = "200" ]; then vert "✓ Anthropic accepte la clé (HTTP 200)."; else rouge "✗ HTTP $code : la clé ne fonctionne pas."; exit 1; fi
