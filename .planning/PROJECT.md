# Savepoint - Weekly Backup Orchestrator

## Problem Statement

Manual weekly backup routines are tedious and error-prone. The process involves multiple steps across different systems (Notion exports, Google Drive, Linux server, VeraCrypt encrypted drives) that must be executed in a specific order with proper dependency management.

## User

Single user (developer) managing personal backups across:
- Windows 11 PC (primary machine)
- Linux server on local network
- External VeraCrypt-encrypted hard drive

## Solution

A Windows 11 desktop application that:
1. Shows a dashboard with backup status and staleness indicators
2. Allows selective execution of backup steps
3. Orchestrates the backup workflow with proper dependencies
4. Provides dry-run mode, progress tracking, and detailed summaries

## Tech Stack

- **Framework**: Avalonia.FuncUI (F# functional UI)
- **Theme**: Fluent Design (Windows 11 native look)
- **File Sync**: Robocopy (Windows built-in, delta sync)
- **Linux Access**: SSH/SCP
- **Encryption**: VeraCrypt CLI integration

---

## Dashboard

The home screen displays real-time status of all backup components:

### Connectivity Status
| Component | States |
|-----------|--------|
| VeraCrypt (B:) | Mounted / Unmounted / Not detected |
| Linux Server | Connected / Unreachable |

### Staleness Indicators

| Source | Green | Orange | Red |
|--------|-------|--------|-----|
| Notion Backup | < 1 week | 1-2 weeks | > 2 weeks |
| Google Takeout | < 2 months | 2-3 months | > 3 months |
| Linux Folders (each) | < 1 week | 1-2 weeks | > 2 weeks |

### Staleness Detection
- **Notion**: Scan `Google Drive/takeout/` for `notion*.zip` files, check latest timestamp
- **Google Takeout**: Scan takeout folder for Google export archives
- **Linux Folders**: Check last sync timestamp for each configured folder

---

## Backup Workflow

### Dependency Chain

```
┌─────────────────────────────────────────────────────────────┐
│                     STAGE 1: Gather                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Notion Export              Linux Backups                   │
│  (manual browser            (SCP from server)               │
│   trigger, watch for        ─────────────────►              │
│   new zip in takeout)       Copy to G-Drive folder          │
│         │                            │                      │
│         ▼                            │                      │
│  Copy to G-Drive folder              │                      │
│         │                            │                      │
│         └──────────┬─────────────────┘                      │
│                    ▼                                        │
├─────────────────────────────────────────────────────────────┤
│                     STAGE 2: Sync                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Local G-Drive folder ──Robocopy──► B:\G-Drive\             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Step Execution
- User selects which steps to run from dashboard
- Steps execute in dependency order
- Each step can be run independently if dependencies are satisfied

---

## Features

### Core Features

1. **Status Dashboard**
   - Pre-flight checklist built into home screen
   - Real-time connectivity checks
   - Visual staleness indicators (color-coded)

2. **Selective Execution**
   - Choose which backup steps to run
   - Run individual steps or full workflow

3. **Dry Run Mode**
   - Preview what would be copied/changed
   - No actual file operations
   - Available from day 1

4. **Progress Tracking**
   - Progress bar for overall operation
   - Expandable live log panel
   - Real-time file operation details

5. **Summary Report**
   - Files added
   - Files updated
   - Files removed
   - Total size transferred

6. **Graceful Error Handling**
   - Failures don't stop entire backup
   - Prompt: "Continue without these files?"
   - Unreachable sources shown as disabled/skipped

### VeraCrypt Integration

- Auto-detect if volume is mounted
- Always unmount first, then remount fresh
- Password prompt at runtime (not saved)
- Mount to drive letter B:
- Uses VeraCrypt CLI (`veracrypt.exe`)

### Linux Server Integration

- SSH/SCP connection
- Configurable folder list (add/remove via UI)
- Per-folder staleness tracking
- Connection status on dashboard

---

## Configuration

### Stored Settings

```
Linux Server:
  - Host/IP
  - Username
  - Authentication (key path or password prompt)
  - Folder list (paths to backup)

Paths:
  - Local Google Drive folder
  - Notion takeout subfolder
  - Google Takeout subfolder
  - VeraCrypt volume file/partition path
  - Mount letter (B:)

Destination:
  - B:\G-Drive\ (mirrors local Google Drive)
```

### Runtime Inputs
- VeraCrypt password (entered each session, not stored)

---

## Destination Structure

```
B:\ (VeraCrypt encrypted partition)
└── G-Drive\
    └── (mirrors local Google Drive folder structure)
        ├── takeout\
        │   ├── notion-2024-01-15.zip
        │   ├── notion-2024-01-22.zip
        │   └── google-takeout-20240120.zip
        ├── Documents\
        ├── Photos\
        └── ...
```

---

## Success Criteria

1. Dashboard accurately shows staleness of all backup sources
2. Can selectively run backup steps with proper dependency ordering
3. Dry run accurately previews changes without modifying files
4. Robocopy sync is fast (delta-only) and provides detailed summary
5. VeraCrypt mount/unmount works seamlessly with password prompt
6. Linux SCP backup works for all configured folders
7. Failures are handled gracefully with user prompts
8. Full backup workflow can complete unattended once started

---

## Out of Scope (for now)

- Scheduled/automated backups (manual trigger only)
- Cloud API integration (using local sync folders instead)
- Backup verification/integrity checking
- Backup versioning/rotation
- Multi-user support
