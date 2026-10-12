# Agent verification

On October 12, 2026, SkillSync synchronized a temporary Git registry into a
temporary project using `SKILLSYNC_HOME`. The installed directories were
`.claude/skills/skillsync-proof/` and `.agents/skills/skillsync-proof/`.

The skill body contained a marker absent from the invocation prompt. Both
agents returned exactly that marker without modifying any files:

| Agent | Version | Result |
| --- | --- | --- |
| Claude Code | 2.1.296 | Loading and explicit invocation confirmed |
| Codex CLI | 0.159.2 | Loading and explicit invocation confirmed in a read-only sandbox |
| Cursor | CLI unavailable in this environment | Live test still required |

These checks use project locations equivalent to the personal locations used
by default. They do not prove reloading within an existing session, automatic
invocation based on the description, or execution of every third-party script
bundled with a skill.

To reproduce, install a test skill with a known response in a temporary
project, then invoke it explicitly in each agent. Keep the marker only in the
skill body. Repeat the check after a major agent version change. Tests that call
a model are opt-in and do not run in the normal test suite.

Official references:
[Codex](https://learn.chatgpt.com/docs/build-skills),
[Claude Code](https://code.claude.com/docs/en/skills),
[Cursor](https://cursor.com/help/customization/skills).

CI also validates executables on Linux, Windows and macOS, in x64 and ARM64.
Each runner installs a real binary from a local HTTP server, activates a native
task on its ephemeral account, waits for successful synchronization, removes
the task, updates the binary, and performs complete removal. Shell profiles
and skill directories remain temporary; native tasks are created only on
ephemeral runners, never on the development machine.

Contribution tests use a real local registry for validation and a simulated
executor to verify branch creation, push and GitHub arguments. They do not
publish test PRs to an external repository.
