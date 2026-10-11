# Development

Use TDD for every feature and behavioural fix: write a check, run it and observe
the expected failure, implement the smallest change, then run it green before
refactoring. Keep checks with their feature and use temporary directories.

Keep the vertical slices in Features/ and the shared Git, validation, storage
and installation responsibilities separate. Avoid additional frameworks or
generic layers without a concrete need.

Run the checks on Linux, Windows and macOS. Installer checks must not change the
developer's real shell profiles, user PATH or installed skills.
