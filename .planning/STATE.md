# Savepoint - Project State

## Current Status

**Phase**: Phase 1 Complete
**Last Updated**: 2026-01-25

---

## Phase Progress

| Phase | Status | Progress |
|-------|--------|----------|
| 1 - Foundation & Dashboard | Complete | 100% |
| 2 - VeraCrypt & Robocopy | Not Started | 0% |
| 3 - Linux Integration | Not Started | 0% |
| 4 - Orchestration | Not Started | 0% |

---

## Current Phase Details

### Phase 1: Foundation & Dashboard (COMPLETE)

| Task | Status |
|------|--------|
| Create Avalonia.FuncUI project | Done |
| Set up F# project structure | Done |
| Implement main window | Done |
| Create staleness detection service | Done |
| Build status card components | Done |
| Implement configuration storage | Done |
| Create settings UI | Done |

### Acceptance Criteria Met
- [x] App launches with Fluent Theme (dark mode)
- [x] Dashboard displays Notion and Google Takeout staleness
- [x] Paths are configurable and persisted

---

## Files Created

| File | Purpose |
|------|---------|
| `src/Savepoint/Savepoint.fsproj` | F# project with NuGet dependencies |
| `src/Savepoint/Program.fs` | App entry point |
| `src/Savepoint/Theme.fs` | Colors, fonts, styles |
| `src/Savepoint/Domain.fs` | Core domain types |
| `src/Savepoint/Services/Config.fs` | Configuration management |
| `src/Savepoint/Services/Staleness.fs` | File scanning & staleness calc |
| `src/Savepoint/Views/Shell.fs` | Main window layout |
| `src/Savepoint/Views/Dashboard.fs` | Overview page |
| `src/Savepoint/Views/Settings.fs` | Configuration UI |
| `src/Savepoint/Views/Components/SourceCard.fs` | Source card component |
| `src/Savepoint/Views/Components/StatusIndicator.fs` | Status dot |
| `src/Savepoint/Views/Components/ProgressBar.fs` | Progress bar |

---

## Decisions Made

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-25 | Use Robocopy over rsync | Windows-native, no WSL required |
| 2026-01-25 | VeraCrypt unmount/remount on each run | Ensures clean state |
| 2026-01-25 | Password not stored | Security - entered each session |
| 2026-01-25 | Phases 2 & 3 can run parallel | Independent integrations |
| 2026-01-25 | Target .NET 9.0 | System has .NET 9 SDK installed |
| 2026-01-25 | Avalonia.FuncUI 1.5.2 | Latest stable version compatible with Avalonia 11.x |

---

## Blockers

None currently.

---

## Recent Progress

| Timestamp | Change |
|-----------|--------|
| 2026-01-26 | Added "Browse" button to path configuration fields with folder picker dialog |
| 2026-01-25 | Removed visible borders from cards, sidebar, and header for cleaner UI |

---

## Notes

- Phase 1 implementation complete
- App builds and runs successfully
- Dark theme matches design inspiration
- Source cards show staleness indicators (Unknown until configured paths exist)
- Settings page allows path configuration
- Configuration persisted to %APPDATA%/Savepoint/config.json
- Ready to begin Phase 2 (VeraCrypt & Robocopy) or Phase 3 (Linux Integration)
