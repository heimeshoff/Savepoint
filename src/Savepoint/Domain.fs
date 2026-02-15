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

    // ============================================
    // VeraCrypt Types (defined before AppConfig)
    // ============================================

    /// Status of a VeraCrypt volume
    type VeraCryptStatus =
        | VCNotConfigured
        | Unmounted
        | Mounted of driveLetter: char
        | VCUnknown
        | VeraCryptError of message: string

    /// Configuration for VeraCrypt volume mounting
    type VeraCryptConfig = {
        ExePath: string option              // Path to VeraCrypt.exe
        PartitionDevicePath: string option  // e.g., "\Device\Harddisk1\Partition1"
        MountLetter: char                   // Target drive letter (e.g., 'B')
    }

    // ============================================
    // Robocopy Types (defined before AppConfig)
    // ============================================

    /// Summary statistics from a Robocopy operation
    type RobocopySummary = {
        FilesTotal: int
        FilesCopied: int
        FilesSkipped: int
        FilesFailed: int
        BytesTotal: int64
        BytesCopied: int64
    }

    /// Progress information during Robocopy sync
    type RobocopyProgressInfo = {
        CurrentFile: string
        FilesProcessed: int
        OverallPercent: int
        IsDryRun: bool
    }

    /// State of sync operations
    type SyncState =
        | SyncIdle
        | Syncing of RobocopyProgressInfo
        | DryRunning of RobocopyProgressInfo
        | SyncCompleted of RobocopySummary
        | SyncError of message: string

    /// Configuration for Robocopy synchronization
    type RobocopyConfig = {
        SourcePath: string           // e.g., "G:\My Drive"
        DestinationPath: string      // e.g., "B:\G-Drive"
    }

    /// Type of file operation detected by Robocopy
    type FileOperation =
        | NewFile      // Green - new file
        | Newer        // Yellow - source newer
        | Older        // Yellow - source older
        | ExtraFile    // Red - will be deleted

    /// A single file entry from Robocopy output
    type FileEntry = {
        Operation: FileOperation
        FullPath: string
        FileName: string
        FileSize: int64 option
    }

    /// Tree structure for displaying sync operations
    type SyncTreeNode =
        | DirectoryNode of name: string * path: string * children: SyncTreeNode list
        | FileNode of FileEntry

    // ============================================
    // Application Configuration
    // ============================================

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
        VeraCrypt: VeraCryptConfig option       // VeraCrypt volume configuration
        Robocopy: RobocopyConfig option         // Robocopy sync configuration
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

    // ============================================
    // Download Types
    // ============================================

    /// Download result for a single folder (simplified for UI display)
    type DownloadResult = {
        FolderName: string
        FilesDownloaded: int
        FilesFailed: int
        ErrorMessage: string option
    }

    /// Detailed download progress info
    type DownloadProgressInfo = {
        FolderName: string
        CurrentFile: string
        CurrentFileIndex: int
        TotalFiles: int
        BytesDownloaded: int64
        TotalBytes: int64
        Percent: int
    }

    /// State of download operations for the dashboard
    type DownloadState =
        | Idle
        | Downloading of DownloadProgressInfo
        | Completed of results: DownloadResult list
        | Error of message: string

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
        VeraCrypt = None
        Robocopy = None
    }
