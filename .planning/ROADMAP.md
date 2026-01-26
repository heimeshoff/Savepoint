# Savepoint - Roadmap

## Overview

| Phase | Focus | Key Deliverable |
|-------|-------|-----------------|
| 1 | Foundation & Dashboard | App shows staleness status |
| 2 | VeraCrypt & Robocopy | Local sync pipeline works |
| 3 | Linux Integration | Remote backup works |
| 4 | Orchestration | Full workflow with error handling |

---

## Phase 1: Foundation & Dashboard

**Goal**: Working app shell with status dashboard

### Tasks
- [ ] Create Avalonia.FuncUI project with Fluent Theme
- [ ] Set up F# project structure (src/Savepoint)
- [ ] Implement main window with dashboard layout
- [ ] Create staleness detection service
  - [ ] Scan for Notion zip files (notion*.zip)
  - [ ] Scan for Google Takeout archives
  - [ ] Calculate age and return status (green/orange/red)
- [ ] Build status card components with color coding
- [ ] Implement configuration storage (JSON)
- [ ] Create settings UI for path configuration

### Acceptance Criteria
- App launches with Fluent Theme
- Dashboard displays Notion and Google Takeout staleness
- Paths are configurable and persisted

### Requirements Covered
- R1: Avalonia.FuncUI scaffolding
- R3: Staleness indicators (partial - local sources)
- R11: Configuration persistence

---

## Phase 2: VeraCrypt & Robocopy Integration

**Goal**: Local backup pipeline working

### Tasks
- [ ] Implement VeraCrypt CLI wrapper
  - [ ] Detect if volume is mounted
  - [ ] Unmount command
  - [ ] Mount command with password
- [ ] Add password input dialog (runtime, not stored)
- [ ] Add VeraCrypt status to dashboard
- [ ] Implement Robocopy wrapper
  - [ ] Build command with delta sync flags
  - [ ] Parse output for progress
  - [ ] Support dry-run mode (/L flag)
- [ ] Create progress tracking UI
  - [ ] Progress bar component
  - [ ] Expandable live log panel
- [ ] Implement summary report
  - [ ] Parse Robocopy output
  - [ ] Display added/updated/removed counts
- [ ] Add Run and Dry Run buttons to UI

### Acceptance Criteria
- Can mount/unmount VeraCrypt with password prompt
- Robocopy syncs G-Drive to B:\G-Drive\
- Dry run shows what would change without changing
- Progress bar and log show real-time status
- Summary shows file counts after sync

### Requirements Covered
- R4: VeraCrypt integration
- R6: Robocopy sync
- R7: Dry-run mode
- R8: Progress bar and live log
- R9: Summary report

---

## Phase 3: Linux Server Integration ✓ COMPLETE

**Goal**: Full backup workflow operational

### Tasks
- [x] Implement SSH connection service
  - [x] Test connectivity (ping/ssh)
  - [x] Support key-based auth
  - [x] Support password auth
- [x] Implement SCP file transfer
  - [x] Download files/folders
  - [x] Progress callback
  - [x] Error handling
- [x] Add Linux server status to dashboard
- [x] Create folder configuration UI
  - [x] Add/remove remote paths
  - [x] Name local destination folders
- [x] Implement per-folder staleness tracking
  - [x] Store last sync timestamps
  - [x] Display staleness per folder
- [x] Add Linux backup execution
  - [x] Copy selected folders
  - [x] Handle partial failures

### Acceptance Criteria ✓
- Dashboard shows Linux server connectivity
- Can configure multiple folders to backup
- Each folder shows staleness status
- SCP transfers files with progress
- Failures are reported, don't crash app

### Requirements Covered
- R2: Connectivity status (Linux)
- R3: Staleness indicators (Linux folders)
- R5: SSH/SCP connection

---

## Phase 4: Workflow Orchestration

**Goal**: Complete, polished backup experience

### Tasks
- [ ] Add step selection UI
  - [ ] Checkboxes for each backup step
  - [ ] Select all / deselect all
- [ ] Implement dependency-aware execution
  - [ ] Stage 1: Gather (Notion, Linux) - parallel
  - [ ] Stage 2: Sync (Robocopy) - after gather
- [ ] Implement graceful error handling
  - [ ] Catch step failures
  - [ ] Prompt: "Continue without these files?"
  - [ ] Track skipped steps in summary
- [ ] Full workflow execution
  - [ ] Single "Run Backup" button
  - [ ] Execute all selected steps in order
- [ ] Edge case handling
  - [ ] VeraCrypt already mounted
  - [ ] Linux unreachable at start (disable steps)
  - [ ] Disk space checks
- [ ] Final UI polish

### Acceptance Criteria
- Can select which steps to run
- Steps execute in correct dependency order
- Errors prompt for continue/abort
- Full backup runs end-to-end
- Summary includes all steps and any skipped items

### Requirements Covered
- R10: Graceful error handling
- R12: Selective step execution

---

## Dependencies

```
Phase 1 (Foundation)
    │
    ▼
Phase 2 (VeraCrypt & Robocopy) ◄──── Phase 3 (Linux)
    │                                      │
    └──────────────┬───────────────────────┘
                   ▼
            Phase 4 (Orchestration)
```

Phase 2 and 3 can be worked in parallel after Phase 1.
Phase 4 requires both Phase 2 and 3 complete.
