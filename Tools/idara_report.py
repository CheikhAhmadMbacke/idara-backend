#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Envoie un rapport de chantier depuis le SERVEUR, avec le compte Idara.

Les identifiants sont lus dans /etc/idara/idara.env et ne quittent jamais la
machine : ni argument de ligne de commande, ni sortie, ni journal.

Usage : idara_report.py <destinataire> <sujet> <fichier_corps.txt> [piece_jointe...]

Deployer :  scp Idara.API/Tools/idara_report.py idara@178.105.202.80:~/

------------------------------------------------------------------------------
2026-10-01 - bascule vers Namecheap Private Email. Trois corrections :

1. Le serveur etait lu sous la cle « EmailSettings__SmtpHost », qui n'a JAMAIS
   existe dans /etc/idara/idara.env (la vraie cle est « __SmtpServer »). Le
   script retombait donc en dur sur smtp.gmail.com sans que rien ne le dise :
   tant que le compte etait Gmail, ca marchait par coincidence. Au premier
   changement de messagerie il aurait echoue a l'authentification, en accusant
   le mot de passe. On lit desormais la bonne cle, on tolere l'ancienne, et on
   REFUSE d'inventer un serveur par defaut.

2. L'identifiant de session n'est plus l'expediteur : on ouvre la session avec
   la boite (contact@pyranil.com) et on expedie depuis l'alias
   (idara@pyranil.com). Meme repli que EmailSettings.EffectiveUsername dans le
   code C# - si le script se trompait la ou l'application a raison, il ne
   prouverait rien.

3. Un en-tete Reply-To, pour qu'une reponse revienne a l'alias.
------------------------------------------------------------------------------
"""
import mimetypes
import os
import smtplib
import ssl
import sys
from email.message import EmailMessage
from email.utils import formataddr, formatdate


def env(path="/etc/idara/idara.env"):
    conf = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            conf[k.strip()] = v.strip().strip('"').strip("'")
    return conf


def main():
    if len(sys.argv) < 4:
        print("usage: idara_report.py <to> <subject> <body_file> [attachment...]",
              file=sys.stderr)
        return 2
    to, subject, body_file = sys.argv[1], sys.argv[2], sys.argv[3]
    attachments = sys.argv[4:]

    conf = env()
    sender = conf.get("EmailSettings__SenderEmail")
    password = conf.get("EmailSettings__SenderPassword")
    sender_name = conf.get("EmailSettings__SenderName") or "Idara"

    # Pas de valeur par defaut : un serveur devine est un envoi qui part au
    # mauvais endroit, ou qui echoue en accusant le mot de passe.
    host = (conf.get("EmailSettings__SmtpServer")
            or conf.get("EmailSettings__SmtpHost"))
    port = int(conf.get("EmailSettings__SmtpPort") or "587")

    # Memes replis que EmailSettings.EffectiveUsername / EffectiveReplyTo.
    username = conf.get("EmailSettings__SmtpUsername") or sender
    reply_to = conf.get("EmailSettings__ReplyTo") or sender

    if not sender or not password:
        print("identifiants SMTP absents de /etc/idara/idara.env", file=sys.stderr)
        return 1
    if not host:
        print("EmailSettings__SmtpServer absent de /etc/idara/idara.env",
              file=sys.stderr)
        return 1
    if port == 465:
        # Le script SAIT faire du SSL implicite, pas l'application. Reussir la
        # ou EmailService.cs resterait bloque donnerait un faux vert.
        print("port 465 (SSL implicite) : l'application ne sait faire que du "
              "STARTTLS. Utiliser 587.", file=sys.stderr)
        return 1

    with open(body_file, encoding="utf-8") as f:
        body = f.read()

    msg = EmailMessage()
    msg["From"] = formataddr((sender_name, sender))
    msg["To"] = to
    msg["Reply-To"] = reply_to
    msg["Subject"] = subject
    msg["Date"] = formatdate(localtime=True)
    msg.set_content(body)

    for path in attachments:
        if not os.path.exists(path):
            print("piece jointe introuvable : " + path, file=sys.stderr)
            return 1
        ctype, _ = mimetypes.guess_type(path)
        maintype, subtype = (ctype or "application/octet-stream").split("/", 1)
        with open(path, "rb") as f:
            msg.add_attachment(f.read(), maintype=maintype, subtype=subtype,
                               filename=os.path.basename(path))

    with smtplib.SMTP(host, port, timeout=90) as s:
        s.ehlo()
        s.starttls(context=ssl.create_default_context())
        s.ehlo()
        s.login(username, password)
        s.send_message(msg)
    print("envoye a {} depuis {} ({} piece(s) jointe(s))".format(
        to, sender, len(attachments)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
