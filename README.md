# felix-launcher-bootstrap

Le **démarreur** du Felix Launcher : un tout petit programme (Native AOT, pas besoin d'installer .NET)
qui installe le launcher, le met à jour, le lance — et gère son installation (dossier, raccourcis, désinstallation).
Windows, Linux et macOS.

## Pour les joueurs — télécharger
https://github.com/Felix-Mcorg/felix-launcher-bootstrap/releases/latest

| Système | Fichier | À savoir |
|---|---|---|
| Windows | `FelixLauncher-Setup.exe` | Double-clic (si SmartScreen : « Informations complémentaires » puis « Exécuter quand même ») |
| Linux | `FelixLauncher-Setup-linux-x64.zip` | Décompresser, puis `chmod +x FelixLauncher-Setup` et `./FelixLauncher-Setup` |
| macOS Apple Silicon (M1+) | `FelixLauncher-Setup-osx-arm64.zip` | Décompresser, puis dans un terminal : `xattr -d com.apple.quarantine FelixLauncher-Setup` et `./FelixLauncher-Setup` |
| macOS Intel | `FelixLauncher-Setup-osx-x64.zip` | Idem |

Le démarreur n'est pas signé par Apple/Microsoft (ça coûte cher) : les systèmes affichent donc un avertissement la première fois.

## Premier lancement
Un petit assistant demande, en console (Entrée = valeur proposée) :
1. le **dossier d'installation** ;
2. les **raccourcis** (bureau et menu) ;
3. les **mises à jour automatiques**.

Ensuite il télécharge le launcher (empreinte SHA-256 vérifiée), s'installe et lance le launcher.
Le fichier téléchargé peut être supprimé : le démarreur s'est copié dans le dossier d'installation.

## Modifier l'installation ou désinstaller
- **Depuis le launcher** : Paramètres > Installation (déplacer le dossier, mises à jour automatiques, raccourcis, désinstaller).
- **Depuis le démarreur** : pendant la mise à jour, appuie sur **M** (ou lance `FelixBootstrap --menu`) pour ouvrir le menu.

### Commandes (sans interface)
| Commande | Effet |
|---|---|
| *(aucune)* | Met à jour (si activé) puis lance le launcher |
| `--menu` | Menu d'options (dossier, mises à jour, raccourcis, réparation, désinstallation) |
| `--update` | Cherche une mise à jour même si les mises à jour automatiques sont coupées |
| `--set auto-update on\|off` | Active ou coupe les mises à jour automatiques |
| `--set shortcuts on\|off`, `--shortcuts create\|remove` | Raccourcis |
| `--move-install <dossier>` | Déplace l'installation (mods et mondes ne bougent pas) |
| `--uninstall [--delete-game-data]` | Désinstalle. Les mods et mondes sont **conservés** sauf avec `--delete-game-data` |
| `--yes` | Ne pose aucune question · `--no-launch` : ne lance pas le launcher · `--wait-pid <n>` : attend la fin du processus n |

## Où sont les fichiers ?
| | Windows | macOS | Linux |
|---|---|---|---|
| Installation (par défaut) | `%LOCALAPPDATA%\FelixLauncher` | `~/Library/Application Support/FelixLauncher` | `~/.local/share/FelixLauncher` |
| Choix du joueur (`bootstrap.json`) | `%APPDATA%\FelixLauncher` | `~/Library/Application Support/FelixLauncher Settings` | `~/.config/felix-launcher` |

```
<installation>/
├─ app/1.0.12/FelixLauncher(.exe) ...   version courante
├─ app/1.0.11/...                        version précédente (retour en arrière)
├─ bootstrap/FelixBootstrap(.exe)        copie du démarreur (cible des raccourcis)
├─ current.json   settings.json   shortcuts.created
├─ tmp/
└─ logs/bootstrap.log
```
`bootstrap.json` est volontairement **hors** de l'installation : il retient où elle se trouve, même après un déplacement.

## Numéro de version (automatique)
Chaque envoi sur `main` publie une release : fichier `VERSION` (ex. `1.0`) + numéro d'exécution → `1.0.1`, `1.0.2`…
Le numéro est injecté à la compilation (`-p:Version=…`) et affiché par `--version`. Pour passer à `1.1.x`, modifie `VERSION`.

## Contrat avec le dépôt `felix-launcher`
Chaque release du launcher contient, pour chaque système :
| Élément | Détail |
|---|---|
| Tag | `vX.Y.Z` |
| `FelixLauncher-<système>.zip` | `<système>` = `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64` ; l'exécutable (`FelixLauncher.exe` ou `FelixLauncher`) est **à la racine** du zip |
| `FelixLauncher-<système>.zip.sha256` | SHA-256 du zip (sans lui, l'installation est refusée) |

Arguments donnés au launcher : `--bootstrapped`, `--install-root <dossier>`, `--bootstrapper <chemin>`,
`--bootstrap-version <v>`, `--bootstrap-config <chemin de bootstrap.json>`.

## Pour les développeurs
SDK **.NET 10**. `dotnet test` fonctionne partout. Compiler en Native AOT demande, en local, la chaîne de compilation C++
du système (Visual Studio « Desktop C++ » / `clang` + `zlib1g-dev` / Xcode) ; sur GitHub, tout est déjà prêt.
