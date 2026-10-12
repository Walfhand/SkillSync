# SkillSync

An early open source **C# / .NET 10** tool, licensed under **MIT**, for sharing a team's skills across Claude Code, Codex and Cursor.

The primary distribution is a **standalone executable**: users do not need to install the .NET SDK or runtime. **Git is still required** to synchronize the library. The .NET SDK is only needed to develop and build the project.

The name is provisional. This is a new .NET implementation inspired by the need presented by [SkillCrew](https://github.com/yoanbernabeu/skillcrew); it is not affiliated with SkillCrew or the agent vendors.

## How it works

A Git repository holds the team's library. Each developer connects the CLI to that repository. The CLI follows the selected branch and installs files in the agents' documented directories:

| Agent | Destination |
| --- | --- |
| Claude Code | `~/.claude/skills/<name>/` |
| Codex and Cursor | `~/.agents/skills/<name>/` — one shared copy |

There is no server, SkillSync account, database or AI model call. Team members collaborate through their Git host's access controls and pull requests. Project-specific skills can stay directly in the project's repository.

## Try it from source

Prerequisites: .NET 10 SDK and Git on the `PATH`. Run the following commands from the project root.

```sh
dotnet build SkillSync.slnx
dotnet run --project tests/SkillSync.Checks
dotnet run --project src/SkillSync -- --help
```

To create a sample library, copy `examples/registry` to a new directory, initialize a Git repository there with a `main` branch, and create its first commit. You can then publish it to your Git host.

```sh
dotnet run --project src/SkillSync -- init /absolute/path/to/the-repository
dotnet run --project src/SkillSync -- status
dotnet run --project src/SkillSync -- sync
dotnet run --project src/SkillSync -- watch --interval 15
```

`init` and `sync` write to the agents' personal directories. Automated checks use temporary directories exclusively.

## Distribute a standalone executable

The `Standalone` profile bundles the runtime and dependencies into a `skillsync` executable (`skillsync.exe` on Windows). The license and guide are kept alongside the binary. This is a self-contained, single-file deployment; the runtime may extract its native libraries to its cache at startup.

To build it on a machine with the .NET 10 SDK:

```sh
dotnet publish src/SkillSync --configuration Release --runtime linux-x64 -p:PublishProfile=Standalone --output artifacts/standalone/linux-x64
```

Replace `linux-x64` with the desired target:

| System | Intel/AMD 64-bit | ARM 64-bit |
| --- | --- | --- |
| Linux with glibc | `linux-x64` | `linux-arm64` |
| Windows | `win-x64` | `win-arm64` |
| macOS | `osx-x64` | `osx-arm64` |

Each binary targets its own operating system and architecture. Native OS dependencies must be present; Alpine/musl is not a supplied target. Updating the bundled runtime requires rebuilding and redistributing the tool with an up-to-date SDK. Native AOT can be evaluated if download size or startup time warrants it.

CI produces these six distributions and checks the installers and executable startup for each runner. A version tag publishes the archives and their SHA-256 checksums to [GitHub Releases](https://github.com/Walfhand/SkillSync/releases).

## Installing on the PATH

Prerequisite: Git. No .NET SDK or runtime is required. The scripts download the archive matching the operating system and architecture, verify its SHA-256 checksum, and install the binary in a user directory.

On Linux or macOS, with Bash or Zsh:

```sh
curl -fsSL https://raw.githubusercontent.com/Walfhand/SkillSync/v0.1.0-preview.6/scripts/install.sh -o /tmp/skillsync-install.sh
bash /tmp/skillsync-install.sh --version v0.1.0-preview.6
```

The binary is installed in `~/.local/bin`. The script adds that directory to the PATH in `~/.bashrc`, `~/.bash_profile` on macOS with Bash, or `~/.zshrc` with Zsh (respecting `ZDOTDIR`). Open a new terminal afterward. You can customize the destinations with `--install-dir /absolute/path` and `--profile /absolute/path/to/profile`. For another shell, install the binary manually and configure its PATH.

On Windows, in PowerShell:

```powershell
Invoke-WebRequest -UseBasicParsing https://raw.githubusercontent.com/Walfhand/SkillSync/v0.1.0-preview.6/scripts/install.ps1 -OutFile "$env:TEMP\skillsync-install.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\skillsync-install.ps1" -Version v0.1.0-preview.6
```

The binary is installed in `%LOCALAPPDATA%\SkillSync\bin` and added to the user Path. Open a new terminal afterward. `-InstallDir` changes the destination and `-NoPathUpdate` leaves the existing PATH unchanged. The ExecutionPolicy change above applies only to the installer process.

You can inspect the scripts before running them. They fail without replacing the binary if the download, SHA-256 checksum or archive contents are invalid. Running the script again replaces the version and keeps a single PATH entry. Stop any `skillsync watch` process before updating.

The version is selected explicitly here because this is a prerelease. Without a version argument, the script uses GitHub's latest **stable release**; this requires a stable release to have been published. Archives can also be downloaded and installed manually from Releases.

Once installed, run these commands from any directory:

```sh
skillsync init git@github.com:my-team/skills.git
skillsync sync
skillsync status
```

To update to this prerelease: `skillsync update --version v0.1.0-preview.6`. On Windows, replacement waits for the current process to exit; the result is written to `~/.skillsync/maintenance.log`. Stop open `watch` processes and disable `autosync` before updating on Windows to release the binary; re-enable the task afterward if needed.

`skillsync remove` removes unchanged managed skills, the synchronization task, PATH entries created by the installers, and the binary. Personal files and backups are preserved. Pre-existing PATH entries are not removed. Versions through preview.4 did not record PATH ownership: run the new installer again to record the profiles before using automatic maintenance. On Windows, a PATH entry that existed before receipts were introduced may require manual removal.

## Alternative: install as a .NET tool

```sh
dotnet pack src/SkillSync --configuration Release --output artifacts/packages
dotnet tool install --global SkillSync.Cli --version 0.1.0-preview.6 --add-source ./artifacts/packages
skillsync --help
```

The package is local; it has not been published to NuGet. Its name must be checked before publication. This installation method requires .NET 10, unlike the standalone distribution described above.

## Commands

```sh
skillsync init git@github.com:my-team/skills.git
skillsync init https://github.com/my-team/skills.git --branch main --agent codex cursor
skillsync sync
skillsync status
skillsync watch --interval 15
skillsync uninstall
```

- `init` connects the library and performs the first synchronization. Without `--agent`, all three agents are targeted. Changing the connection configuration requires uninstalling first.
- `sync` fetches the branch and applies a specific Git commit. A network failure preserves already installed skills; no new installation is attempted offline.
- `status` shows the commit, last synchronization, and any missing or modified copies.
- `watch` synchronizes immediately and then every 15 minutes by default while the process is running. `Ctrl+C` stops it. Use `autosync enable` to automate synchronization without keeping a terminal open.
- `uninstall` removes only unchanged managed skills. `remove` also removes the scheduler task, PATH entries owned by the installers, and the standalone binary. Backups and metadata remain in `~/.skillsync/`. For the .NET tool, use `dotnet tool uninstall --global SkillSync.Cli`.

## Selection and team rules

```sh
skillsync init git@github.com:my-team/skills.git --category architecture --skill code-review --team backend
skillsync list
skillsync select --category architecture
skillsync select --skill code-review pr-description
skillsync select --all
skillsync select --revision FULL_GIT_COMMIT
skillsync select --follow-branch
```

The selection is persisted. Named skills and categories form a union; a category includes its subcategories. Without a selection, all non-blocked skills are installed. Unknown selections are rejected before any change. If an explicitly selected name disappears from the registry, change the selection to resume synchronization.

The optional `skillsync.json` file at the registry root defines teams:

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

All three arrays are required, and their names must exist in the registry. A skill cannot have multiple statuses within a team. Required skills are added to every selection; recommendations appear in `list`; blocked skills are excluded from `--all` and rejected when explicitly requested. `select --team NAME` changes the team.

A team can use a protected branch, such as `--branch approved`, to distribute only reviewed changes. `--revision` pins a full, immutable commit; `select --follow-branch` resumes branch tracking. Approval and access rights are managed by Git: these local conventions are not access controls and do not prevent users from changing their configuration.

## Automatic synchronization

```sh
skillsync autosync enable --interval 15
skillsync autosync status
skillsync autosync disable
```

The standalone executable uses a systemd user timer on Linux, a LaunchAgent on macOS, and a user task on Windows. Tasks run at login and periodically, without administrator privileges. Linux requires a working systemd user session. Git must be available in the task's environment, and authentication must work non-interactively.

`autosync status` queries the scheduler and displays the last result recorded in `~/.skillsync/last-sync.json`. A network failure preserves installed copies. `watch` remains available for synchronization limited to a terminal's lifetime.

## Create and propose skills

From a local clone of the library:

```sh
skillsync new architecture-review --repo /path/to/registry --category architecture --description "Review architecture decisions."
skillsync validate --repo /path/to/registry
skillsync propose --repo /path/to/registry --title "Add architecture review" --base main
```

`new` creates a directory and its `SKILL.md` without overwriting an existing skill. Add any required scripts, references and assets to that directory. `validate` inspects local files, including uncommitted changes and team rules.

`propose` requires an authenticated GitHub CLI (`gh`), an HTTPS/SSH `origin` remote, and a configured Git identity. The local base branch must match `origin`; pending changes must belong to `skills/` or `skillsync.json`. Renames are rejected to avoid including unexpected files. The command validates the library, creates a branch, commits the changes, pushes it, and opens a draft PR. If PR creation fails after the push, the branch is kept and the error explains how to retry. Git hooks are disabled during these operations. Other Git hosts remain usable for synchronization; PR creation currently supports GitHub only.

## Backups and rollback

```sh
skillsync backups list
skillsync rollback
skillsync backups prune --keep 10 --yes
```

`rollback` restores the installation preceding the last content change. A synchronization with no changes does not replace that restore point. Copies and backups are verified before restoration; local edits block the operation. The restored revision is pinned so the automatic task cannot immediately reapply the reverted version. Use `select --follow-branch` to resume updates.

Pruning requires `--yes`. It preserves the current restore point and transactions containing files recovered after an interruption. Older backups without restoration metadata are not deleted automatically.

## Registry format

```text
skills/
  architecture/
    ddd-review/
      SKILL.md
      scripts/        (optional)
      references/     (optional)
      assets/         (optional)
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

Categories can contain multiple skills and multiple levels of subcategories. A category may have a `README.md` for its documentation. Skills directly under `skills/` are also supported.

A directory containing `SKILL.md` is a skill: all its files and subdirectories belong to it, including any `SKILL.md` templates in its resources. Other files must be placed inside a skill, except for category `README.md` files.

The `name` field must match the skill's directory, use lowercase letters, digits and hyphens, and contain at most 64 characters. `description` is required, must be non-empty, and is limited to 1024 characters. **The name must be unique across the entire library**, including across categories.

Categories organize the repository; at installation, each skill is placed directly under its name in the agent's directory. For example, `skills/architecture/ddd-review/` becomes `~/.agents/skills/ddd-review/`. Moving a skill to another category without changing its name or files preserves its identity and installation.

```md
---
name: code-review
description: Review a code change for bugs and regressions. Use when a code review is requested.
---

Read the change and its callers. Report actionable issues with file locations.
```

The library distributes all skills discovered under `skills/`, subject to the configured selection and team rules. Removing an entire skill from the registry removes it from managed destinations at the next synchronization, after backing it up. Removing only its `SKILL.md` while leaving other ordinary files causes an error and preserves existing installations. An empty library removes all its managed skills. Categories, selections and team rules are managed by the CLI. Agent-specific marketplaces are not generated.

Limits: 1000 files and 25 MiB under `skills/` per library, including category README files; 5 MiB per file, 128 KiB per entry-point `SKILL.md`, and a maximum frontmatter nesting depth of 32. YAML aliases, symbolic links, submodules, non-portable paths and case collisions are rejected. Git LFS is not expanded: its files remain Git pointers.

## File protection

Collisions with personal skills and local edits to a managed skill block the entire synchronization before any destination changes. There is no option to force overwriting them. To contribute, copy your changes into the registry, open a pull request, then move the locally modified copy out of the managed directory before synchronizing.

Previous copies are moved to `~/.skillsync/transactions/<id>/backup/`, preserving their agent paths. An interruption before state is saved triggers recovery on the next command. Any files from the new installation are preserved under `recovered/` before that restoration. A lock prevents two SkillSync commands from modifying installations simultaneously.

Backups are not purged automatically. They may contain private content and consume disk space. Use `backups prune` to clean them up and `rollback` to restore the previous verifiable installation. Writes across multiple skills are not simultaneously atomic for other applications: journal-based recovery protects interrupted updates. Avoid synchronizing while an agent is executing a skill's script.

## Security and compatibility

The registry is a trusted source: protect its branch and review changes, especially scripts. SkillSync reads Git blobs without checkout, filters or execution of registry scripts. Agents may execute those scripts when using a skill.

Authentication uses Git and the mechanisms already configured on the machine. HTTPS URLs with embedded credentials and unencrypted transports are rejected. Git invocations pass arguments separately and disable hooks and unauthorized transports. No telemetry is sent.

Managed paths must not contain symbolic links or junctions. Directory moves assume they can take place on the same filesystem; separate mounts may cause synchronization to fail and roll back. The journal is not a guarantee against every hardware failure. The program relies on the user account's protections; it does not defend against a malicious process with the same privileges modifying files concurrently.

The file format is shared, but commands, tools, permissions and triggers may differ between agents. Cloud environments do not automatically receive files from a local machine. Installing a skill does not prove that an agent loaded or used it.

Reference documentation: [Agent Skills](https://agentskills.io/specification), [Claude Code](https://code.claude.com/docs/en/skills), [Codex](https://learn.chatgpt.com/docs/build-skills), [Cursor](https://cursor.com/docs/skills).

## Development and contributing

One console project contains the CLI and engine; a second project provides executable checks without an additional test framework. The only production dependencies are `System.CommandLine` for the CLI and `YamlDotNet` for frontmatter.

The code uses a lightweight Vertical Slice organization: each command groups its arguments, validations and use case in `Features/`. Shared components have separate responsibilities:

```text
src/SkillSync/
  Program.cs                  Entry point
  Cli/
    CliApp.cs                 Composition and command registration
    CommandOutput.cs          Output, errors and exit codes
  Features/
    Init.cs                   Connect to a library
    Sync.cs                   Synchronize an installation
    Status.cs                 Inspect local state
    Watch.cs                  Periodic synchronization
    Uninstall.cs              Remove managed skills
    Selection.cs              Catalogue, selection and pinned revision
    Authoring.cs              Local creation and validation
    Propose.cs                Git contribution and GitHub PRs
    Backups.cs                Rollback and cleanup
    AutoSync.cs               Scheduling commands
    Maintenance.cs            CLI update and removal
  Registry/
    GitRegistry.cs            Git access and registry file reading
    SkillCatalog.cs           Skill discovery across categories
    SkillValidator.cs         Name and YAML frontmatter validation
    RegistryConfig.cs         Connection configuration and constraints
    Snapshot.cs               Library revision contents
  Installation/
    SkillInstaller.cs         Preparation, conflicts, backups and recovery
    Journal.cs                Transaction journal data
  Storage/
    LocalFiles.cs             Safe paths, locks, hashes and JSON writes
    StateStore.cs             Installation state reading and validation
    SyncState.cs              Persisted installation data
  Scheduling/                 Native scheduler definitions and operations
  Maintenance/                PATH ownership and installation receipts
  Runtime/                    Bounded, cancellable external tool execution
  Agents/
    AgentTargets.cs           Supported agents and destinations
```

`CliApp` explicitly constructs components and registers commands. Slices orchestrate their operations; they share one `SkillInstaller` to apply files and recover interrupted transactions. This engine knows neither CLI commands nor Git. `Watch` composes the `Sync` and `Status` use cases.

Tests are grouped by feature under `tests/SkillSync.Checks/Features/`, with dedicated checks for recovery, registry validation and the complete CLI. The runner, assertions and temporary Git repositories are separate. Every feature or fix follows TDD: write a check in the relevant group, observe its failure, implement the minimum, then pass it before refactoring. This rule is also recorded in `AGENTS.md`.

Existing commands and installation paths remain compatible. State from older versions can be read; configuration gains optional fields for selection, team and revision. Older CLIs cannot read these new fields: rolling back skills does not roll back the binary. This organization does not require a mediation framework, dependency injection container or generic repository layer.

```sh
dotnet build SkillSync.slnx --configuration Release
dotnet run --project tests/SkillSync.Checks --configuration Release --no-build
dotnet pack src/SkillSync --configuration Release --no-build --output artifacts/packages
```

CI runs these commands natively on all six Linux/Windows/macOS and x64/ARM64 combinations, tests installers with a local HTTP server, and checks distribution archives and the real binary installation/update/removal lifecycle. It produces six standalone archives and their SHA-256 checksums; version tags publish them to GitHub Releases. Checks use a real temporary Git registry and cover installation, updates, removal, personal files, local edits, deletions, simulated network failure, missing files, invalid metadata, malicious links and interruption recovery. Creating a local link is skipped on Windows when it requires privileges; Git-tracked links remain tested.

Installer tests cover downloads, SHA-256 failures, malicious archives, invalid versions, missing Git and duplicate-free PATH setup. They never modify real shell profiles or the user's PATH. Live agent tests and their limits are documented in [docs/verification.md](docs/verification.md). To isolate checks, `SKILLSYNC_HOME` selects the root of managed files; it does not change the system home directory or agent settings.
