# Vérification des agents

Le 12 octobre 2026, un registre Git temporaire a été synchronisé par SkillSync
dans un projet temporaire via SKILLSYNC_HOME. Les dossiers installés étaient
.claude/skills/skillsync-proof/ et .agents/skills/skillsync-proof/.

Le corps du skill contenait un marqueur absent du prompt d'invocation. Les deux
agents ont renvoyé exactement ce marqueur, sans modification de fichiers :

| Agent | Version | Résultat |
| --- | --- | --- |
| Claude Code | 2.1.296 | Chargement et invocation explicite confirmés |
| Codex CLI | 0.159.2 | Chargement et invocation explicite confirmés, sandbox lecture seule |
| Cursor | Non installé dans cet environnement | Essai réel restant à réaliser |

Ces essais utilisent les emplacements projet équivalents aux emplacements
personnels distribués par défaut. Ils ne prouvent pas le rechargement pendant
une session déjà ouverte, le déclenchement automatique par description, ni le
fonctionnement de chaque script tiers contenu dans un skill.

Pour reproduire, installer un skill de test avec une réponse connue dans un
projet temporaire, puis l'invoquer explicitement dans chaque agent. Garder le
marqueur uniquement dans le corps du skill. Refaire l'essai après un changement
important de version d'un agent. Les essais appelant un modèle sont volontaires
et ne s'exécutent pas dans la suite de tests normale.

Références officielles :
[Codex](https://learn.chatgpt.com/docs/build-skills),
[Claude Code](https://code.claude.com/docs/en/skills),
[Cursor](https://cursor.com/help/customization/skills).

La CI valide aussi les exécutables sur Linux, Windows et macOS, en x64 et ARM64.
Chaque runner installe un vrai binaire depuis un serveur HTTP local, active une
tâche native sur son compte éphémère, attend une synchronisation réussie, retire
la tâche, met à jour le binaire et effectue le retrait complet. Les profils de
shell et les dossiers de skills restent temporaires ; les tâches natives sont
créées uniquement sur les runners éphémères, jamais sur la machine de développement.

Les tests de contribution utilisent un vrai registre local pour les validations
et un exécuteur simulé pour vérifier la création de branche, le push et les
arguments GitHub. Ils ne publient pas de PR de test sur un dépôt externe.
