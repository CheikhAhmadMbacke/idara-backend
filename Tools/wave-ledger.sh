#!/usr/bin/env bash
# =============================================================================
# wave-ledger.sh — le registre du compte marchand, pour une journée.
#
#     bash ~/wave-ledger.sh              # aujourd'hui
#     bash ~/wave-ledger.sh 2026-09-18   # une autre date
#
# 🔑 À quoi il sert vraiment : c'est la SEULE source qui donne le frais
# RÉELLEMENT prélevé par Wave, ligne à ligne. Ni l'événement de webhook, ni
# l'objet session ne le portent. Sans cette lecture, la grille de taux du
# back-office resterait une supposition — et c'est exactement ainsi qu'une
# majoration fausse a pu tourner quatre mois (§256).
#
# Il montre aussi les mouvements faits À LA MAIN depuis l'application Wave
# Business, ceux qui creusent la réserve sans qu'aucune écriture d'Idara ne
# les explique (§112).
#
# Lecture seule : ce script ne déplace pas un franc.
# =============================================================================

set -u

ENV_FILE="/etc/idara/idara.env"
DATE="${1:-$(date -u +%Y-%m-%d)}"

rouge() { printf '\033[31m%s\033[0m\n' "$*"; }
vert()  { printf '\033[32m%s\033[0m\n' "$*"; }
gras()  { printf '\033[1m%s\033[0m\n' "$*"; }

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
  rouge "Wave__ApiKey absent de $ENV_FILE"
  exit 1
fi

EN_TETES=(-H "Authorization: Bearer $CLE")
if [ -n "${SIGNING:-}" ]; then
  TS=$(date +%s)
  SIG=$(printf '%s' "$TS" | openssl dgst -sha256 -hmac "$SIGNING" | sed 's/^.* //')
  EN_TETES+=(-H "Wave-Signature: t=${TS},v1=${SIG}")
fi

echo
gras "═══ Registre Wave du $DATE ═══"
echo

REP=$(curl -s -m 30 -o /tmp/wave_ledger.$$ -w '%{http_code}' \
  "${EN_TETES[@]}" "https://api.wave.com/v1/transactions?date=${DATE}" 2>/dev/null)
CORPS=$(cat /tmp/wave_ledger.$$ 2>/dev/null); rm -f /tmp/wave_ledger.$$

if [ "$REP" != "200" ]; then
  rouge "HTTP $REP — $CORPS"
  exit 1
fi

# Pas de `jq` garanti sur le serveur : python3 l'est, et il est déjà utilisé
# ailleurs par l'exploitation.
printf '%s' "$CORPS" | python3 -c '
import json, sys

data = json.load(sys.stdin)
lignes = data.get("items") or data.get("result") or []

if not lignes:
    print("  Aucun mouvement ce jour-la.")
    raise SystemExit(0)

print("  %-20s %-22s %12s %8s %12s  %s" % (
    "HEURE", "TYPE", "MONTANT", "FRAIS", "SOLDE APRES", "NOTRE REFERENCE"))
print("  " + "-" * 100)

total_in = total_out = total_fee = 0
for t in lignes:
    def nb(v):
        try: return int(float(v))
        except (TypeError, ValueError): return 0
    montant = nb(t.get("amount"))
    frais   = nb(t.get("fee"))
    horo    = (t.get("timestamp") or "")[11:19]
    typ     = (t.get("transaction_type") or "?")[:22]
    ref     = t.get("client_reference") or "-"
    print("  %-20s %-22s %12d %8d %12s  %s" % (
        horo, typ, montant, frais, t.get("balance") or "?", ref))
    if montant >= 0: total_in += montant
    else: total_out += -montant
    total_fee += abs(frais)

print("  " + "-" * 100)
print("  Entrees %d F | Sorties %d F | Frais %d F" % (total_in, total_out, total_fee))

# Le taux effectif, qui est TOUT l objet de cette lecture.
print()
for t in lignes:
    def nb(v):
        try: return int(float(v))
        except (TypeError, ValueError): return 0
    montant, frais = nb(t.get("amount")), abs(nb(t.get("fee")))
    if montant and frais:
        base = abs(montant) + (frais if montant < 0 else 0)
        sens = "sortie" if montant < 0 else "entree"
        print("  Taux reel (%s de %d F) : %d F de frais = %.3f %%"
              % (sens, abs(montant), frais, 100.0 * frais / base))

taxe = [t for t in lignes if t.get("government_tax_amount")]
if taxe:
    print()
    print("  ATTENTION : taxe d Etat presente sur %d ligne(s) -" % len(taxe))
    for t in taxe:
        print("     %s  montant=%s  payee_par_wave=%s" % (
            t.get("transaction_id"), t.get("government_tax_amount"),
            t.get("government_tax_paid_by_wave")))
    print("  A repercuter dans la grille : c est le piege de la RUTEL (§258).")
'

echo
gras "Reporter le taux reel dans SuperAdmin -> Reglages plateforme -> Frais."
echo "Ne jamais le supposer : l arrondi de Wave n est pas forcement le notre."
echo
