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

## Release version lines

- Keep all public release notes in English and use the same approved text in `CHANGELOG.md`, `.github/release-notes.md`, `build.yaml`, and the Jellyfin catalog entry.
- `aniLiberty-v2`: Jellyfin 10.11, plugin 2.x; `jellyfin-12`: Jellyfin 12, plugin 3.x.
- Use an explicit approved four-part version; never derive it from the repository-wide highest tag.
- Build and verify packages before tagging or publishing. Never overwrite published versions.
- Preserve historical catalog entries. Since `targetAbi` is a minimum, keep the newest higher-ABI release numerically above every lower-ABI release, including during publication.
