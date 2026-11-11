# Gemini Diag Direct (eeePC)

Assistant de diagnostic automobile basé sur Gemini, pour l'eeePC 1101HA (Windows 10 32 bits).
Gemini connaît tes véhicules et ton matériel grâce à la fiche garage, lit tes relevés (CSV OBDeeepc, FORScan…)
et peut lancer des commandes sur le PC avec ton accord.

## Installation

1. Télécharge `GeminiDiagDirect-win32.zip` dans la dernière version (onglet **Releases** du dépôt).
2. Décompresse-le dans un dossier où tu peux écrire (par exemple `C:\Diag\GeminiDiag`).
3. Lance `GeminiDiagDirect.exe`. Au premier lancement, l'appli te demande ta clé API Gemini
   (à créer sur https://aistudio.google.com/apikey).

Ensuite, à chaque démarrage, l'appli vérifie s'il existe une nouvelle version et te propose de l'installer.

## Fichiers sur le PC (jamais envoyés sur GitHub)

| Fichier | Rôle |
|---|---|
| `C:\Diag\gemini.cfg` | Ta clé API (`cle_api=...`). Bouton 🔑 pour la changer. |
| `C:\Diag\garage.txt` | Ta fiche garage : véhicules, moteurs, ECU, outils, câble. Bouton 🚗 Mon garage pour la modifier. Relue à chaque message. |
| `C:\Diag\Logs_Input` | Dossier des relevés que Gemini peut chercher et lire tout seul. |
| `C:\Diag\Gemini_History` | Historique des conversations et rapports exportés. |

## Compiler soi-même

Double-clique sur `compiler.bat` : il utilise le compilateur C# fourni avec Windows (.NET Framework 4),
aucune installation nécessaire.

## Publier une nouvelle version

Change `VERSION` en haut de `GeminiDiagDirect.cs` et pousse sur `main` : GitHub compile l'exe et publie la version
automatiquement. L'icône est générée par `outils_icone.py`.
