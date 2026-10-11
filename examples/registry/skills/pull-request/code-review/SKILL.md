---
name: code-review
description: Review a code change for correctness, regressions and missing tests. Use when someone asks for a code review; do not invoke for ordinary implementation work.
---

Read the changed code and its callers before drawing conclusions.

1. Identify changes in behavior and the callers they affect.
2. Check error handling, input validation and any risk of data loss.
3. Check whether tests cover the meaningful changed behavior.
4. Report only actionable findings, with a file location and the consequence.
5. If no issue is found, say so and state what was not verified.

Do not modify files or publish comments unless the user requests it.
