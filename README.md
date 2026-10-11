# SkillSync

Première version d'un outil open source en **C# / .NET 10**, sous licence **MIT**, pour partager les skills d'une équipe entre Claude Code, Codex et Cursor.

La distribution principale est un **exécutable autonome** : aucun SDK ni runtime .NET à installer sur la machine de l'utilisateur. **Git reste nécessaire** pour synchroniser la bibliothèque. Le SDK .NET sert uniquement à développer et compiler le projet.

Le nom est provisoire. Le code est une nouvelle implémentation .NET, inspirée du besoin présenté par [SkillCrew](https://github.com/yoanbernabeu/skillcrew) ; ce projet n'est pas affilié à SkillCrew ou aux éditeurs des agents.

## Fonctionnement

Un dépôt Git contient la bibliothèque de l'équipe. Chaque développeur connecte la CLI à ce dépôt. La CLI suit la branche choisie et installe des fichiers dans les dossiers documentés des agents :

| Agent | Destination |
| --- | --- |
| Claude Code | `~/.claude/skills/<nom>/` |
| Codex et Cursor | `~/.agents/skills/<nom>/` — une copie partagée |

Il n'y a aucun serveur, compte SkillSync, base de données ou appel à un modèle IA. Les membres collaborent avec les accès et les pull requests de leur hébergeur Git. Les skills propres à un projet peuvent rester directement dans le dépôt du projet.

## Essayer depuis les sources

Prérequis : SDK .NET 10 et Git dans le `PATH`. Les commandes ci-dessous partent de la racine du projet.

```sh
dotnet build SkillSync.slnx
dotnet run --project tests/SkillSync.Checks
dotnet run --project src/SkillSync -- --help
```

Pour créer une bibliothèque d'essai, copiez `examples/registry` dans un nouveau dossier, initialisez-y un dépôt Git avec une branche `main`, puis créez son premier commit. Vous pouvez ensuite le publier sur votre hébergeur Git.

```sh
dotnet run --project src/SkillSync -- init /chemin/absolu/vers/le-depot
dotnet run --project src/SkillSync -- status
dotnet run --project src/SkillSync -- sync
dotnet run --project src/SkillSync -- watch --interval 15
```

`init` et `sync` écrivent dans les dossiers personnels des agents. Les vérifications automatisées utilisent exclusivement des dossiers temporaires.

## Distribuer un exécutable autonome

Le profil `Standalone` embarque le runtime et les dépendances dans un exécutable `skillsync` (`skillsync.exe` sous Windows). La licence et le guide restent à côté du binaire. Il s'agit d'une publication self-contained en fichier unique ; le runtime peut extraire ses bibliothèques natives dans son cache au démarrage.

Pour compiler, depuis une machine équipée du SDK .NET 10 :

```sh
dotnet publish src/SkillSync --configuration Release --runtime linux-x64 -p:PublishProfile=Standalone --output artifacts/standalone/linux-x64
```

Remplacez `linux-x64` par la cible souhaitée :

| Système | Intel/AMD 64 bits | ARM 64 bits |
| --- | --- | --- |
| Linux avec glibc | `linux-x64` | `linux-arm64` |
| Windows | `win-x64` | `win-arm64` |
| macOS | `osx-x64` | `osx-arm64` |

Chaque binaire cible son système et son architecture. Les dépendances natives de l'OS doivent être présentes ; Alpine/musl n'est pas une cible fournie. Le runtime embarqué se met à jour en recompilant et redistribuant l'outil avec un SDK à jour. Native AOT pourra être évalué si la taille du téléchargement ou le temps de démarrage le justifie.

La CI est configurée pour produire ces six distributions et vérifier le lancement du binaire correspondant à chaque runner. Les binaires sont disponibles comme artefacts de compilation ; la publication automatique de versions dans GitHub Releases reste à ajouter.

## Installer dans le PATH

Après téléchargement et extraction de la distribution correspondant à votre machine, ouvrez un terminal dans son dossier.

Sous Linux ou macOS :

```sh
mkdir -p "$HOME/.local/bin"
install -m 755 ./skillsync "$HOME/.local/bin/skillsync"
export PATH="$HOME/.local/bin:$PATH"
skillsync --help
```

Si `~/.local/bin` n'est pas déjà dans votre `PATH`, ajoutez la ligne `export PATH="$HOME/.local/bin:$PATH"` à votre fichier de configuration du shell (`~/.bashrc` pour Bash ou `~/.zshrc` pour Zsh) afin de la conserver dans les nouveaux terminaux.

Sous Windows, dans PowerShell :

```powershell
$installDir = Join-Path $env:LOCALAPPDATA 'SkillSync\bin'
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item .\skillsync.exe $installDir
```

Ajoutez ensuite `%LOCALAPPDATA%\SkillSync\bin` au `Path` utilisateur dans les variables d'environnement Windows, puis ouvrez un nouveau terminal.

Une fois le dossier dans le `PATH`, ces commandes fonctionnent depuis n'importe quel dossier :

```sh
skillsync init git@github.com:mon-equipe/skills.git
skillsync sync
skillsync status
```

Pour remplacer une installation autonome, arrêtez `watch` et remplacez l'exécutable par celui de la nouvelle version. Pour la retirer, exécutez d'abord `skillsync uninstall`, puis supprimez le binaire installé ; les sauvegardes restent conservées.

## Alternative : installer comme outil .NET

```sh
dotnet pack src/SkillSync --configuration Release --output artifacts/packages
dotnet tool install --global SkillSync.Cli --version 0.1.0-preview.3 --add-source ./artifacts/packages
skillsync --help
```

Le package est local : il n'est pas publié sur NuGet. Son nom devra être vérifié avant une publication. Ce mode d'installation nécessite .NET 10, contrairement à la distribution autonome décrite ci-dessus.

## Commandes

```sh
skillsync init git@github.com:mon-equipe/skills.git
skillsync init https://github.com/mon-equipe/skills.git --branch main --agent codex cursor
skillsync sync
skillsync status
skillsync watch --interval 15
skillsync uninstall
```

- `init` connecte la bibliothèque et réalise la première synchronisation. Sans `--agent`, les trois agents sont ciblés. Une configuration différente nécessite d'abord une désinstallation.
- `sync` récupère la branche et applique un commit Git précis. Une panne réseau conserve les skills déjà installés ; aucune nouvelle installation hors connexion n'est tentée.
- `status` indique le commit, la dernière synchronisation et les copies manquantes ou modifiées.
- `watch` synchronise immédiatement puis toutes les 15 minutes par défaut, pendant que le processus reste actif. `Ctrl+C` l'arrête. Aucun démarrage automatique avec la session de l'agent ou avec l'ordinateur n'est installé.
- `uninstall` retire uniquement les skills gérés qui n'ont pas été modifiés. Les sauvegardes et les métadonnées restent dans `~/.skillsync/`. Supprimez ensuite le binaire autonome installé, ou utilisez `dotnet tool uninstall --global SkillSync.Cli` pour l'installation via .NET.

## Format du registre

```text
skills/
  architecture/
    ddd-review/
      SKILL.md
      scripts/        (facultatif)
      references/     (facultatif)
      assets/         (facultatif)
    solid-review/
      SKILL.md
  pull-request/
    code-review/
      SKILL.md
    pr-description/
      SKILL.md
  commit-message/
    SKILL.md
```

Les catégories peuvent contenir plusieurs skills et plusieurs niveaux de sous-catégories. Une catégorie peut avoir un `README.md` pour sa documentation. Les skills directement sous `skills/` restent acceptés.

Un dossier contenant `SKILL.md` est un skill : tous ses fichiers et sous-dossiers lui appartiennent, y compris les éventuels modèles de `SKILL.md` dans ses ressources. Les autres fichiers doivent être placés dans un skill, à l'exception des `README.md` des catégories.

Le champ `name` doit correspondre au dossier du skill, utiliser des minuscules, chiffres et tirets, et compter au maximum 64 caractères. `description` est obligatoire, non vide, et limitée à 1024 caractères. **Le nom doit être unique dans toute la bibliothèque**, même entre catégories.

Les catégories organisent le dépôt ; à l'installation, chaque skill est placé directement sous son nom dans le dossier de l'agent. Ainsi, `skills/architecture/ddd-review/` devient `~/.agents/skills/ddd-review/`. Déplacer un skill dans une autre catégorie sans modifier son nom ou ses fichiers conserve son identité et son installation.

```md
---
name: code-review
description: Review a code change for bugs and regressions. Use when a code review is requested.
---

Read the change and its callers. Report actionable issues with file locations.
```

La bibliothèque distribue tous les skills découverts sous `skills/`. Supprimer entièrement un skill du registre le retire des destinations gérées à la prochaine synchronisation, après sauvegarde. Retirer seulement son `SKILL.md` en laissant d'autres fichiers ordinaires provoque une erreur et conserve les installations existantes. Une bibliothèque vide retire tous ses skills gérés. Les marketplaces Claude, le filtrage par catégorie, la sélection par équipe et les statuts obligatoire/recommandé/bloqué ne sont pas implémentés dans cette version.

Limites : 1000 fichiers et 25 MiB sous `skills/` par bibliothèque, README des catégories compris ; 5 MiB par fichier, 128 KiB par `SKILL.md` d'entrée, frontmatter imbriqué à 32 niveaux au maximum. Les alias YAML, liens symboliques, sous-modules, chemins non portables et collisions de casse sont refusés. Git LFS n'est pas développé : ses fichiers restent des pointeurs Git.

## Protection des fichiers

Les collisions avec des skills personnels et les modifications locales d'un skill géré bloquent toute la synchronisation avant de changer les destinations. Il n'existe pas d'option pour forcer leur écrasement. Pour contribuer, copiez vos modifications dans le registre, ouvrez une pull request, puis déplacez la copie locale modifiée hors du dossier géré avant de synchroniser.

Les anciennes copies sont déplacées dans `~/.skillsync/transactions/<identifiant>/backup/`, avec leur chemin d'agent. Une interruption avant l'enregistrement de l'état déclenche une restauration à la prochaine commande. Les éventuels fichiers de la nouvelle installation sont conservés sous `recovered/` avant cette restauration. Un verrou empêche deux commandes SkillSync de modifier simultanément les installations.

Les sauvegardes ne sont pas purgées automatiquement. Elles peuvent contenir du contenu privé et consommer de l'espace disque. Il n'y a pas encore de commande de retour arrière manuel ; conservez une sauvegarde avant une restauration à la main. Les écritures de plusieurs skills ne sont pas simultanément atomiques pour les autres applications : la récupération par journal protège les mises à jour interrompues. Évitez de synchroniser pendant qu'un agent exécute un script du skill.

## Sécurité et compatibilité

Le registre est une source de confiance : protégez sa branche et faites relire les changements, particulièrement les scripts. SkillSync lit les blobs Git sans checkout, filtres ou exécution des scripts du registre. Les agents peuvent toutefois exécuter ces scripts lorsqu'ils utilisent un skill.

L'authentification passe par Git et les mécanismes déjà configurés sur la machine. Les URL HTTPS avec identifiants intégrés et les transports non chiffrés sont refusés. Les invocations Git utilisent des arguments séparés, désactivent les hooks et les transports non autorisés. Aucune télémétrie n'est envoyée.

Les chemins gérés ne doivent pas contenir de liens symboliques ou de jonctions. Les déplacements de dossiers supposent qu'ils peuvent être effectués sur le même système de fichiers ; des montages distincts peuvent faire échouer une synchronisation, qui sera alors restaurée. Le journal n'est pas une garantie contre toutes les pannes matérielles. Le programme s'appuie sur les protections du compte utilisateur ; il ne défend pas contre un autre processus malveillant ayant les mêmes droits et modifiant les fichiers en même temps.

Le format des fichiers est partagé, mais les commandes, outils, permissions et déclenchements peuvent varier selon l'agent. Les environnements cloud ne reçoivent pas automatiquement les fichiers d'une machine locale. L'installation d'un skill ne prouve pas qu'un agent l'a chargé ou utilisé.

Documentation de référence : [Agent Skills](https://agentskills.io/specification), [Claude Code](https://code.claude.com/docs/en/skills), [Codex](https://learn.chatgpt.com/docs/build-skills), [Cursor](https://cursor.com/docs/skills).

## Développer et contribuer

Un projet console contient la CLI et le moteur ; un second projet fournit des vérifications exécutables sans framework de test supplémentaire. Les seules dépendances de production sont `System.CommandLine` pour la CLI et `YamlDotNet` pour le frontmatter.

Le code suit une organisation Vertical Slice légère : chaque commande regroupe ses arguments, ses validations et son cas d'usage dans `Features/`. Les composants communs ont des responsabilités distinctes :

```text
src/SkillSync/
  Program.cs                  Point d'entrée
  Cli/
    CliApp.cs                 Composition et enregistrement des commandes
    CommandOutput.cs          Affichage, erreurs et codes de sortie
  Features/
    Init.cs                   Connexion à une bibliothèque
    Sync.cs                   Synchronisation d'une installation
    Status.cs                 Inspection de l'état local
    Watch.cs                  Synchronisation périodique
    Uninstall.cs              Retrait des skills gérés
  Registry/
    GitRegistry.cs            Accès Git et lecture des fichiers du registre
    SkillCatalog.cs           Découverte des skills dans les catégories
    SkillValidator.cs         Validation des noms et du frontmatter YAML
    RegistryConfig.cs         Configuration de connexion et ses contraintes
    Snapshot.cs               Contenu d'une version de la bibliothèque
  Installation/
    SkillInstaller.cs         Préparation, conflits, sauvegardes et récupération
    Journal.cs                Données du journal de transaction
  Storage/
    LocalFiles.cs             Chemins sûrs, verrou, empreintes et écritures JSON
    StateStore.cs             Lecture et validation de l'état d'installation
    SyncState.cs              Données persistées d'une installation
  Agents/
    AgentTargets.cs           Agents pris en charge et destinations
```

`CliApp` construit explicitement les composants et enregistre les commandes. Les slices pilotent leurs opérations ; elles partagent un seul `SkillInstaller` pour appliquer les fichiers et récupérer les transactions interrompues. Ce moteur ne connaît ni les commandes CLI ni Git. `Watch` compose les cas d'usage `Sync` et `Status`.

Les tests sont regroupés par fonctionnalité sous `tests/SkillSync.Checks/Features/`, avec des vérifications dédiées à la récupération, à la validation du registre et à la CLI complète. Le runner, les assertions et les dépôts Git temporaires sont séparés. Une correction doit ajouter sa vérification dans le groupe concerné.

Les arguments CLI, les chemins d'installation et le format JSON de l'état local sont conservés. Aucun framework de médiation, conteneur d'injection de dépendances ou couche de repositories génériques n'est nécessaire pour ce découpage.

```sh
dotnet build SkillSync.slnx --configuration Release
dotnet run --project tests/SkillSync.Checks --configuration Release --no-build
dotnet pack src/SkillSync --configuration Release --no-build --output artifacts/packages
```

La CI fournie est configurée pour exécuter ces commandes sur Linux, Windows et macOS, puis publier les exécutables autonomes en artefacts de compilation. Les vérifications portent sur un vrai registre Git temporaire : installation, mise à jour, désinstallation, fichiers personnels, modifications locales, suppressions, panne réseau simulée, fichiers manquants, métadonnées invalides, liens malveillants et récupération d'interruption. La création d'un lien local est ignorée sous Windows lorsqu'elle nécessite des privilèges ; les liens suivis dans Git restent testés.

Pour une correction, ajoutez une vérification qui reproduit le problème. Les prochaines étapes utiles sont les essais réels avec les trois agents, les intégrations aux planificateurs des trois OS, puis la gestion facultative des équipes et des versions approuvées.
