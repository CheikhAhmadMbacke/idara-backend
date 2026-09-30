#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""email-selftest.py — prouver que le serveur sait expedier un courriel.

Appele par email-setup-secrets.sh et par email-check.sh. Le mot de passe
arrive par l'ENTREE STANDARD, jamais en argument : un argument est visible
dans `ps` pour tout le monde, et reste dans l'historique du shell (§190).

Pourquoi python et pas `curl` ou `openssl s_client` : le controle doit parler
le MEME protocole que l'application. EmailService.cs utilise
System.Net.Mail.SmtpClient avec EnableSsl = true sur le port 587, c'est-a-dire
EHLO -> STARTTLS -> EHLO -> AUTH -> envoi. `smtplib` fait exactement cela.
Un controle qui se contenterait d'ouvrir le port declarerait « bon » un mot de
passe faux, et un controle qui ne chiffrerait pas serait refuse alors que la
configuration est juste : dans les deux cas il accuse le secret qu'on vient
d'ecrire (faux negatif rencontre sur Wave le 2026-09-17).

Usage :
    printf '%s' "$motdepasse" | python3 email-selftest.py \\
        <hote> <port> <identifiant> <expediteur> <nom> <repondre_a> <destinataire>

Sortie : « ENVOYE|<Message-ID> » sur la sortie standard, ou un message
d'echec sur la sortie d'erreur avec un code de retour non nul.
"""

import email.utils
import smtplib
import ssl
import sys
from email.message import EmailMessage

ATTENDU = 7

if len(sys.argv) - 1 != ATTENDU:
    sys.exit(
        "email-selftest: %d arguments recus, %d attendus "
        "(hote port identifiant expediteur nom repondre_a destinataire)"
        % (len(sys.argv) - 1, ATTENDU)
    )

hote, port, identifiant, expediteur, nom, repondre_a, destinataire = sys.argv[1:8]
motdepasse = sys.stdin.read().strip()

if not motdepasse:
    sys.exit("email-selftest: aucun mot de passe recu sur l'entree standard.")

if port.strip() == "465":
    # Le message doit etre explicite : sur 465 le script python REUSSIRAIT
    # avec SMTP_SSL, et l'application, elle, resterait bloquee. Un controle
    # vert pour une application muette est pire que pas de controle.
    sys.exit(
        "email-selftest: port 465 (SSL implicite). SmtpClient (.NET) ne sait "
        "faire que du STARTTLS et resterait bloque jusqu'au timeout. "
        "Utiliser 587."
    )

msg = EmailMessage()
msg["From"] = email.utils.formataddr((nom, expediteur))
msg["To"] = destinataire
msg["Reply-To"] = repondre_a
msg["Subject"] = "Idara - verification de la messagerie professionnelle"
msg["Date"] = email.utils.formatdate(localtime=True)
msg["Message-ID"] = email.utils.make_msgid(domain=expediteur.split("@")[-1])
msg.set_content(
    "Ce message prouve que le serveur Idara sait expedier par la messagerie\n"
    "professionnelle, dans le protocole exact de l'application.\n"
    "\n"
    "Serveur              : %s:%s (STARTTLS)\n"
    "Session ouverte avec : %s\n"
    "Expediteur affiche   : %s\n"
    "Repondre a           : %s\n"
    "\n"
    "A verifier dans le message recu :\n"
    "  - il n'est PAS dans les indesirables ;\n"
    "  - l'expediteur affiche est bien l'alias, pas la boite technique ;\n"
    "  - dans les en-tetes (Gmail : « Afficher l'original ») :\n"
    "        spf=pass   dkim=pass   dmarc=pass\n"
    % (hote, port, identifiant, expediteur, repondre_a)
)

try:
    with smtplib.SMTP(hote, int(port), timeout=30) as serveur:
        serveur.ehlo()
        serveur.starttls(context=ssl.create_default_context())  # = EnableSsl sur 587
        serveur.ehlo()
        serveur.login(identifiant, motdepasse)
        serveur.send_message(msg)
except smtplib.SMTPAuthenticationError as exc:
    sys.exit("email-selftest: AUTH REFUSEE - identifiant ou mot de passe. %s" % exc)
except smtplib.SMTPSenderRefused as exc:
    sys.exit(
        "email-selftest: expediteur REFUSE - la boite %s n'a pas le droit "
        "d'expedier au nom de %s. %s" % (identifiant, expediteur, exc)
    )
except Exception as exc:  # noqa: BLE001 - on veut nommer la cause a l'operateur
    sys.exit("email-selftest: ECHEC (%s) : %s" % (type(exc).__name__, exc))

print("ENVOYE|%s" % msg["Message-ID"])
