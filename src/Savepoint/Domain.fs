namespace Savepoint

open System

/// Core domain types for backup management
module Domain =

    /// Represents the staleness level of a backup source
    type StalenessLevel =
        | Fresh     // Within acceptable age
        | Stale     // Getting old, needs attention
        | Critical  // Too old, urgent action needed
        | Unknown   // No backup found or unable to determine

    /// Configuration for staleness thresholds
    type StalenessThresholds = {
        FreshDays: int      // Days until considered fresh
        StaleDays: int      // Days until considered stale (after this: critical)
    }

    /// Type of backup source
    type SourceType =
        | NotionExport
        | GoogleTakeout
        | LinuxServer of host: string

    /// Configuration for a Linux server backup folder
    type LinuxFolder = {
        RemotePath: string      // e.g., "/home/user/backups"
        LocalPath: string       // e.g., "G:\My Drive\linux-backups\server"
        LocalName: string       // Display name, e.g., "Server Backups"
        FilePattern: string     // e.g., "*.tar.gz" to find latest file
    }

    /// Represents a backup source and its current status
    type BackupSource = {
        Name: string
        Description: string
        SourceType: SourceType
        Path: string option
        LastBackup: DateTime option
        Staleness: StalenessLevel
        StorageUsedMB: int64 option
        StorageTotalMB: int64 option
    }

    /// Application configuration
    type AppConfig = {
        GoogleDrivePath: string
        NotionPath: string
        GoogleTakeoutPath: string
        LinuxServerHost: string option
        LinuxServerPort: int                    // Default 22
        LinuxServerUser: string option          // SSH username
        LinuxServerKeyPath: string option       // Path to private key
        LinuxServerPassphrase: string option    // Passphrase for encrypted SSH key
        LinuxServerFolders: LinuxFolder list    // Configured backup folders
    }

    /// Represents a navigation page in the app
    type Page =
        | Overview
        | Sources
        | Settings

    /// Recent activity log entry
    type ActivityStatus =
        | Success
        | Warning
        | Error

    type ActivityEntry = {
        Status: ActivityStatus
        Source: string
        Message: string
        Timestamp: DateTime
    }

    /// Default staleness thresholds
    module Thresholds =
        /// Notion exports: Fresh <7 days, Stale 7-14 days, Critical >14 days
        let notion = { FreshDays = 7; StaleDays = 14 }

        /// Google Takeout: Fresh <60 days, Stale 60-90 days, Critical >90 days
        let googleTakeout = { FreshDays = 60; StaleDays = 90 }

        /// Linux server: Fresh <7 days, Stale 7-14 days, Critical >14 days
        let linuxServer = { FreshDays = 7; StaleDays = 14 }

    /// Calculate staleness level from age and thresholds
    let calculateStaleness (thresholds: StalenessThresholds) (lastBackup: DateTime option) : StalenessLevel =
        match lastBackup with
        | None -> Unknown
        | Some date ->
            let age = DateTime.Now - date
            if age.TotalDays < float thresholds.FreshDays then Fresh
            elif age.TotalDays < float thresholds.StaleDays then Stale
            else Critical

    /// Default application configuration
    let defaultConfig: AppConfig = {
        GoogleDrivePath = @"G:\"
        NotionPath = @"G:\My Drive\notion"
        GoogleTakeoutPath = @"G:\My Drive\google-takeout"
        LinuxServerHost = None
        LinuxServerPort = 22
        LinuxServerUser = None
        LinuxServerKeyPath = None
        LinuxServerPassphrase = None
        LinuxServerFolders = []
    }
