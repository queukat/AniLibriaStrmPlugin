# Repository Instructions

## Changelog and Jellyfin Release Notes

`CHANGELOG.md` content is displayed directly inside Jellyfin. Write it for ordinary plugin users, not for repository maintainers.

- Describe only user-visible behavior, fixes, compatibility changes, upgrade requirements, and practical performance or reliability improvements.
- Express technical work through its user impact: faster tasks, lower Jellyfin resource usage, more reliable playback or library updates.
- Do not include implementation details such as class names, algorithms, cache design, pagination, serialization, internal architecture, or file-level changes.
- Do not mention tests, test counts, builds, CI, formatting, analyzers, commits, issue templates, audits, or development process.
- Do not add a `Not Included` section.
- Keep the text concise, natural, and understandable without repository knowledge.
- Include upgrade notes only when the user must restart, reconfigure, migrate, regenerate, or take another action.
- Draft the changelog for user approval before editing `CHANGELOG.md`, changing versions, tagging, publishing, or deploying a release.
