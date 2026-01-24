# Savepoint - Requirements

## V1 Requirements (MVP)

### Foundation
| ID | Requirement | Phase |
|----|-------------|-------|
| R1 | Avalonia.FuncUI app with Fluent Theme scaffolding | 1 |
| R11 | Configuration persistence (JSON file for paths, settings) | 1 |

### Dashboard
| ID | Requirement | Phase |
|----|-------------|-------|
| R2 | Dashboard showing connectivity status (VeraCrypt, Linux) | 1, 3 |
| R3 | Staleness indicators for Notion, Google Takeout, Linux folders | 1, 3 |

### Integrations
| ID | Requirement | Phase |
|----|-------------|-------|
| R4 | VeraCrypt CLI integration (mount/unmount with password prompt) | 2 |
| R5 | SSH/SCP connection to Linux server | 3 |
| R6 | Robocopy sync from local G-Drive to B:\G-Drive\ | 2 |

### Execution
| ID | Requirement | Phase |
|----|-------------|-------|
| R7 | Dry-run mode for all operations | 2 |
| R10 | Graceful error handling with continue prompts | 4 |
| R12 | Selective step execution from dashboard | 4 |

### UI/Reporting
| ID | Requirement | Phase |
|----|-------------|-------|
| R8 | Progress bar with expandable live log | 2 |
| R9 | Summary report (added/updated/removed files) | 2 |

---

## V2 Requirements (Future)

| ID | Requirement | Notes |
|----|-------------|-------|
| V2-1 | Backup verification/integrity checking | Checksums, validation |
| V2-2 | Backup versioning/rotation | Keep N versions, auto-cleanup |
| V2-3 | Scheduled/automated backups | Windows Task Scheduler integration |

---

## Out of Scope

- Cloud API integration (using local sync folders)
- Multi-user support
- Cross-platform (Windows 11 only)

---

## Staleness Rules

| Source | Green | Orange | Red |
|--------|-------|--------|-----|
| Notion Backup | < 1 week | 1-2 weeks | > 2 weeks |
| Google Takeout | < 2 months | 2-3 months | > 3 months |
| Linux Folders | < 1 week | 1-2 weeks | > 2 weeks |

---

## Configuration Schema

```
{
  "googleDrivePath": "C:\\Users\\...\\Google Drive",
  "notionTakeoutSubfolder": "takeout",
  "googleTakeoutSubfolder": "takeout",
  "veraCryptVolumePath": "\\\\?\\Volume{...}",
  "veraCryptMountLetter": "B",
  "linuxServer": {
    "host": "192.168.1.x",
    "username": "user",
    "authMethod": "key|password",
    "keyPath": "C:\\Users\\...\\.ssh\\id_rsa",
    "folders": [
      { "remotePath": "/opt/immich/backup", "localName": "immich" },
      { "remotePath": "/home/user/configs", "localName": "configs" }
    ]
  }
}
```
