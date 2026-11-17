# Gemini Diag Direct (eeePC)

Assistant de diagnostic automobile basé sur Gemini, pour l'eeePC 1101HA (Windows 10 32 bits).
Gemini connaît tes véhicules et ton matériel grâce à la fiche garage, lit tes relevés (CSV OBDeeepc, FORScan…)
et peut lancer des commandes sur le PC avec ton accord.

## Installation

1. Télécharge `GeminiDiagDirect-win32.zip` dans la dernière version (onglet **Releases** du dépôt,
   pas « Source code »). Il ne contient qu'un fichier : `GeminiDiagDirect.exe`.
2. Mets l'exe dans un dossier où tu peux écrire (par exemple `C:\Diag\GeminiDiag`).
3. Lance `GeminiDiagDirect.exe`. Au premier lancement, l'appli te demande ta clé API Gemini
   (à créer sur https://aistudio.google.com/apikey).

Ensuite, à chaque démarrage, l'appli vérifie s'il existe une nouvelle version et te propose de l'installer.

## Fichiers sur le PC (jamais envoyés sur GitHub)

| Fichier | Rôle |
|---|---|
| `C:\Diag\gemini.cfg` | Ta clé API (`cle_api=...`, bouton 🔑 pour la changer) et le dernier modèle choisi (`modele=...`). |
| `C:\Diag\garage.txt` | Ta fiche garage : véhicules, moteurs, ECU, outils, câble. Bouton 🚗 Mon garage pour la modifier. Relue à chaque message. |
| `C:\Diag\Logs_Input` | Dossier des relevés que Gemini peut chercher et lire tout seul. |
| `C:\Diag\Gemini_History` | Historique des conversations (`AAAA-MM-JJ_HHMM - véhicule - sujet.json`) et rapports exportés. |

## Historique des conversations

- Au démarrage, l'appli rouvre la dernière conversation et remet le dernier modèle choisi. 🔄 Nouveau en démarre une vierge.
- Chaque conversation est enregistrée après chaque réponse, dans un fichier nommé avec la date, le véhicule reconnu
  grâce à la fiche garage et le début de la première question.
- 📂 Historique rouvre une ancienne conversation avec le modèle qu'elle utilisait.
- Le contenu des CSV joints ou lus par Gemini n'est pas gardé après l'analyse : ni dans le fichier, ni dans les
  messages suivants envoyés à Gemini. Il peut relire le fichier sur le disque si on lui redemande.

## Compiler soi-même

Double-clique sur `compiler.bat` : il utilise le compilateur C# fourni avec Windows (.NET Framework 4),
aucune installation nécessaire.

## Publier une nouvelle version

Change `VERSION` en haut de `GeminiDiagDirect.cs` et pousse sur `main` : GitHub compile l'exe et publie la version
automatiquement.
