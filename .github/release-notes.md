AniLiberty STRM Performance Update

This update makes library generation and Skip Intro/Outro processing faster and lighter on Jellyfin, especially for larger libraries.

### Performance Improvements
- Faster full-catalog and Favorites library generation.
- Reduced CPU and memory usage during generation and library updates.
- Faster preparation of native Skip Intro/Outro data after Jellyfin starts or scans a library.
- Improved performance during repeated scheduled runs and when multiple library locations are configured.
- Reduced memory usage while processing artwork and plugin-managed library data.

### Upgrade Notes
- Restart Jellyfin after installing the update.
- No settings changes or library regeneration are required.
