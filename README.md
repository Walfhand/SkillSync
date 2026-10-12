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

La CI produit ces six distributions, vérifie les installateurs et le lancement du binaire correspondant à chaque runner. Un tag de version publie les archives et leurs empreintes SHA-256 dans [GitHub Releases](https://github.com/Walfhand/SkillSync/releases).

## Installer dans le PATH

Prérequis : Git. Aucun SDK ou runtime .NET n'est nécessaire. Les scripts téléchargent l'archive adaptée au système et à l'architecture, vérifient son SHA-256 et installent le binaire dans un dossier utilisateur.

Sous Linux ou macOS, avec Bash ou Zsh :

```sh
curl -fsSL https://raw.githubusercontent.com/Walfhand/SkillSync/v0.1.0-preview.5/scripts/install.sh -o /tmp/skillsync-install.sh
bash /tmp/skillsync-install.sh --version v0.1.0-preview.5
```

Le binaire est installé dans `~/.local/bin`. Le script ajoute ce dossier au PATH dans `~/.bashrc`, `~/.bash_profile` sous macOS avec Bash, ou `~/.zshrc` avec Zsh (en respectant `ZDOTDIR`). Ouvrez ensuite un nouveau terminal. Vous pouvez personnaliser les destinations avec `--install-dir /chemin/absolu` et `--profile /chemin/absolu/vers/le/profil`. Pour un autre shell, installez manuellement le binaire et configurez son PATH.

Sous Windows, dans PowerShell :

```powershell
Invoke-WebRequest -UseBasicParsing https://raw.githubusercontent.com/Walfhand/SkillSync/v0.1.0-preview.5/scripts/install.ps1 -OutFile "$env:TEMP\skillsync-install.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\skillsync-install.ps1" -Version v0.1.0-preview.5
```

Le binaire est installé dans `%LOCALAPPDATA%\SkillSync\bin`, ajouté au Path utilisateur. Ouvrez ensuite un nouveau terminal. `-InstallDir` change la destination et `-NoPathUpdate` conserve le PATH existant. Le changement d'ExecutionPolicy ci-dessus concerne uniquement le processus d'installation.

Les scripts sont consultables avant exécution. Ils échouent sans remplacer le binaire si le téléchargement, le SHA-256 ou le contenu de l'archive est invalide. Relancer le script remplace la version et conserve une seule entrée PATH. Arrêtez d'abord tout processus `skillsync watch` avant une mise à jour.

La version est explicitement sélectionnée ici parce qu'il s'agit d'une préversion. Sans argument de version, le script utilise la dernière **version stable** de GitHub ; cette option nécessite qu'une version stable ait été publiée. Les archives peuvent aussi être téléchargées et installées manuellement depuis Releases.

Une fois installé, depuis n'importe quel dossier :

```sh
skillsync init git@github.com:mon-equipe/skills.git
skillsync sync
skillsync status
```

Pour mettre à jour cette préversion : `skillsync update --version v0.1.0-preview.5`. Sous Windows, le remplacement attend la fin du processus ; le résultat est écrit dans `~/.skillsync/maintenance.log`. Arrêtez les processus `watch` ouverts avant une mise à jour.

`skillsync remove` retire les skills gérés non modifiés, la tâche de synchronisation, les entrées PATH créées par les installateurs et le binaire. Les fichiers personnels et les sauvegardes sont conservés. Les entrées PATH préexistantes ne sont pas retirées. Les versions jusqu'à preview.4 ne conservaient pas la propriété des entrées PATH : relancez le nouvel installateur pour enregistrer les profils avant d'utiliser la maintenance automatique.

## Alternative : installer comme outil .NET

```sh
dotnet pack src/SkillSync --configuration Release --output artifacts/packages
dotnet tool install --global SkillSync.Cli --version 0.1.0-preview.5 --add-source ./artifacts/packages
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
- `watch` synchronise immédiatement puis toutes les 15 minutes par défaut, pendant que le processus reste actif. `Ctrl+C` l'arrête. Pour automatiser sans terminal ouvert, utilisez `autosync enable`.
- `uninstall` retire uniquement les skills gérés non modifiés. `remove` retire aussi le planificateur, les entrées PATH appartenant aux installateurs et le binaire autonome. Les sauvegardes et métadonnées restent dans `~/.skillsync/`. Pour l'outil .NET, utilisez `dotnet tool uninstall --global SkillSync.Cli`.

## Sélection et règles d'équipe

```sh
skillsync init git@github.com:mon-equipe/skills.git --category architecture --skill code-review --team backend
skillsync list
skillsync select --category architecture
skillsync select --skill code-review pr-description
skillsync select --all
skillsync select --revision COMMIT_GIT_COMPLET
skillsync select --follow-branch
```

La sélection est persistée. Les noms et catégories forment une union ; une catégorie inclut ses sous-catégories. Sans sélection, tous les skills non bloqués sont installés. Une sélection inconnue est refusée avant toute modification. Si un nom explicitement sélectionné disparaît du registre, modifiez la sélection pour reprendre la synchronisation.

Le fichier facultatif `skillsync.json`, à la racine du registre, définit les équipes :

```json
{
  "teams": {
    "backend": {
      "required": ["code-review"],
      "recommended": ["pr-description"],
      "blocked": ["legacy-deploy"]
    }
  }
}
```

Les trois tableaux sont obligatoires et les noms doivent exister dans le registre. Un skill ne peut pas avoir plusieurs statuts dans une équipe. Les skills requis sont ajoutés à toute sélection ; les recommandations sont affichées par `list` ; les skills bloqués sont exclus de `--all` et refusés lorsqu'ils sont explicitement demandés. `select --team NOM` change l'équipe.

Une équipe peut utiliser une branche protégée, par exemple `--branch approved`, pour distribuer uniquement les changements relus. `--revision` épingle un commit complet et immuable ; `select --follow-branch` reprend le suivi de la branche. L'approbation et les droits d'accès sont ceux de Git : ces conventions locales ne constituent pas un contrôle d'accès ni un mécanisme empêchant un utilisateur de changer sa configuration.

## Synchronisation automatique

```sh
skillsync autosync enable --interval 15
skillsync autosync status
skillsync autosync disable
```

L'exécutable autonome utilise un timer systemd utilisateur sous Linux, un LaunchAgent sous macOS et une tâche utilisateur sous Windows. Les tâches s'exécutent à l'ouverture de session et périodiquement, sans privilèges administrateur. Sous Linux, une session systemd utilisateur fonctionnelle est requise. Git doit être accessible dans l'environnement de la tâche et l'authentification doit fonctionner sans interaction.

`autosync status` interroge le planificateur et affiche le dernier résultat enregistré dans `~/.skillsync/last-sync.json`. Une panne réseau conserve les copies installées. `watch` reste disponible pour une synchronisation limitée à la durée d'un terminal.

## Créer et proposer des skills

Depuis un clone local de la bibliothèque :

```sh
skillsync new architecture-review --repo /chemin/du/registre --category architecture --description "Relire les décisions d'architecture."
skillsync validate --repo /chemin/du/registre
skillsync propose --repo /chemin/du/registre --title "Ajouter la revue d'architecture" --base main
```

`new` crée un dossier et son `SKILL.md` sans écraser un skill existant. Ajoutez les scripts, références et ressources nécessaires dans ce dossier. `validate` inspecte les fichiers locaux, y compris les modifications non commitées et les règles d'équipe.

`propose` nécessite GitHub CLI (`gh`) authentifiée, un remote `origin` HTTPS/SSH et une identité Git configurée. La branche locale de base doit correspondre à `origin` ; les modifications en attente doivent appartenir à `skills/` ou à `skillsync.json`. Les renommages sont refusés pour éviter d'inclure des fichiers inattendus. La commande valide la bibliothèque, crée une branche, commit les modifications, la pousse et ouvre une PR en brouillon. Si la création de PR échoue après le push, la branche est conservée et le message indique comment réessayer. Les hooks Git sont désactivés pendant ces opérations. Les autres hébergeurs Git restent utilisables pour synchroniser ; l'ouverture de PR est actuellement spécifique à GitHub.

## Sauvegardes et retour arrière

```sh
skillsync backups list
skillsync rollback
skillsync backups prune --keep 10 --yes
```

`rollback` restaure l'installation précédant le dernier changement de contenu. Une synchronisation sans changement ne remplace pas ce point de retour. Les copies et sauvegardes sont vérifiées avant restauration ; les modifications locales bloquent l'opération. La révision restaurée est épinglée pour empêcher la tâche automatique de réappliquer immédiatement la version annulée. Utilisez `select --follow-branch` pour reprendre les mises à jour.

Le nettoyage exige `--yes`. Il conserve le point de retour actuel et les transactions contenant des fichiers récupérés après interruption. Les anciennes sauvegardes sans métadonnées de restauration ne sont pas supprimées automatiquement.

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

La bibliothèque distribue tous les skills découverts sous `skills/`. Supprimer entièrement un skill du registre le retire des destinations gérées à la prochaine synchronisation, après sauvegarde. Retirer seulement son `SKILL.md` en laissant d'autres fichiers ordinaires provoque une erreur et conserve les installations existantes. Une bibliothèque vide retire tous ses skills gérés. Les catégories, sélections et règles d'équipe sont gérées par la CLI. Les marketplaces propres aux agents ne sont pas générées.

Limites : 1000 fichiers et 25 MiB sous `skills/` par bibliothèque, README des catégories compris ; 5 MiB par fichier, 128 KiB par `SKILL.md` d'entrée, frontmatter imbriqué à 32 niveaux au maximum. Les alias YAML, liens symboliques, sous-modules, chemins non portables et collisions de casse sont refusés. Git LFS n'est pas développé : ses fichiers restent des pointeurs Git.

## Protection des fichiers

Les collisions avec des skills personnels et les modifications locales d'un skill géré bloquent toute la synchronisation avant de changer les destinations. Il n'existe pas d'option pour forcer leur écrasement. Pour contribuer, copiez vos modifications dans le registre, ouvrez une pull request, puis déplacez la copie locale modifiée hors du dossier géré avant de synchroniser.

Les anciennes copies sont déplacées dans `~/.skillsync/transactions/<identifiant>/backup/`, avec leur chemin d'agent. Une interruption avant l'enregistrement de l'état déclenche une restauration à la prochaine commande. Les éventuels fichiers de la nouvelle installation sont conservés sous `recovered/` avant cette restauration. Un verrou empêche deux commandes SkillSync de modifier simultanément les installations.

Les sauvegardes ne sont pas purgées automatiquement. Elles peuvent contenir du contenu privé et consommer de l'espace disque. Utilisez `backups prune` pour les nettoyer et `rollback` pour restaurer la précédente installation vérifiable. Les écritures de plusieurs skills ne sont pas simultanément atomiques pour les autres applications : la récupération par journal protège les mises à jour interrompues. Évitez de synchroniser pendant qu'un agent exécute un script du skill.

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

Les tests sont regroupés par fonctionnalité sous `tests/SkillSync.Checks/Features/`, avec des vérifications dédiées à la récupération, à la validation du registre et à la CLI complète. Le runner, les assertions et les dépôts Git temporaires sont séparés. Toute fonctionnalité ou correction suit le cycle TDD : écrire la vérification dans le groupe concerné, observer son échec, implémenter le minimum, puis passer au vert avant de refactorer. Cette règle est aussi inscrite dans `AGENTS.md`.

Les arguments CLI, les chemins d'installation et le format JSON de l'état local sont conservés. Aucun framework de médiation, conteneur d'injection de dépendances ou couche de repositories génériques n'est nécessaire pour ce découpage.

```sh
dotnet build SkillSync.slnx --configuration Release
dotnet run --project tests/SkillSync.Checks --configuration Release --no-build
dotnet pack src/SkillSync --configuration Release --no-build --output artifacts/packages
```

La CI exécute ces commandes nativement sur les six couples Linux/Windows/macOS et x64/ARM64, teste les installateurs avec un serveur HTTP local et vérifie les archives de distribution et le cycle réel installation/mise à jour/retrait du binaire. Elle produit six archives autonomes et leurs SHA-256 ; les tags de version les publient dans GitHub Releases. Les vérifications portent sur un vrai registre Git temporaire : installation, mise à jour, désinstallation, fichiers personnels, modifications locales, suppressions, panne réseau simulée, fichiers manquants, métadonnées invalides, liens malveillants et récupération d'interruption. La création d'un lien local est ignorée sous Windows lorsqu'elle nécessite des privilèges ; les liens suivis dans Git restent testés.

Les tests des installateurs couvrent les téléchargements, les erreurs de SHA-256, les archives malveillantes, les versions invalides, Git absent et le PATH sans doublon. Ils ne modifient jamais les profils ou le PATH utilisateur réels. Les essais réels des agents et leurs limites sont décrits dans [docs/verification.md](docs/verification.md). Pour isoler les essais, `SKILLSYNC_HOME` permet de choisir la racine des fichiers gérés ; cette variable ne change pas le dossier personnel du système ni les réglages des agents.
