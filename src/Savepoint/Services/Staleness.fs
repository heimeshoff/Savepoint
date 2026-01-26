namespace Savepoint.Services

open System
open System.IO
open Savepoint.Domain

/// Staleness detection service for backup sources
module Staleness =

    /// Find the most recent file matching a pattern in a directory (searches subdirectories)
    let private findLatestFile (directory: string) (pattern: string) : DateTime option =
        if Directory.Exists(directory) then
            try
                // Search recursively to find files in subdirectories
                let files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)
                if files.Length > 0 then
                    files
                    |> Array.map (fun f -> FileInfo(f).LastWriteTime)
                    |> Array.max
                    |> Some
                else
                    None
            with
            | _ -> None
        else
            None

    /// Get the total size of files matching a pattern (searches subdirectories)
    let private getTotalSize (directory: string) (pattern: string) : int64 option =
        if Directory.Exists(directory) then
            try
                // Search recursively to include files in subdirectories
                let files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)
                if files.Length > 0 then
                    files
                    |> Array.map (fun f -> FileInfo(f).Length)
                    |> Array.sum
                    |> Some
                else
                    None
            with
            | _ -> None
        else
            None

    /// Scan for Notion export zip files
    let scanNotion (notionPath: string) : BackupSource =
        let latestBackup = findLatestFile notionPath "notion*.zip"
        let storageUsed = getTotalSize notionPath "notion*.zip"

        {
            Name = "Notion Archives"
            Description = "Workspace Export"
            SourceType = NotionExport
            Path = Some notionPath
            LastBackup = latestBackup
            Staleness = calculateStaleness Thresholds.notion latestBackup
            StorageUsedMB = storageUsed |> Option.map (fun s -> s / (1024L * 1024L))
            StorageTotalMB = Some 50000L  // 50GB placeholder
        }

    /// Scan for Google Takeout archives
    let scanGoogleTakeout (takeoutPath: string) : BackupSource =
        // Look for takeout-*.zip or any .tgz files
        let latestZip = findLatestFile takeoutPath "takeout*.zip"
        let latestTgz = findLatestFile takeoutPath "*.tgz"

        let latestBackup =
            match latestZip, latestTgz with
            | Some z, Some t -> Some (max z t)
            | Some z, None -> Some z
            | None, Some t -> Some t
            | None, None -> None

        let zipSize = getTotalSize takeoutPath "takeout*.zip" |> Option.defaultValue 0L
        let tgzSize = getTotalSize takeoutPath "*.tgz" |> Option.defaultValue 0L
        let totalSize = if zipSize + tgzSize > 0L then Some (zipSize + tgzSize) else None

        {
            Name = "Google Takeout"
            Description = "Account Export"
            SourceType = GoogleTakeout
            Path = Some takeoutPath
            LastBackup = latestBackup
            Staleness = calculateStaleness Thresholds.googleTakeout latestBackup
            StorageUsedMB = totalSize |> Option.map (fun s -> s / (1024L * 1024L))
            StorageTotalMB = Some 100000L  // 100GB placeholder
        }

    /// Scan a single Linux folder by checking LOCAL files (downloaded to GDrive)
    let scanLinuxFolder (host: string) (folder: LinuxFolder) : BackupSource =
        let latestBackup = findLatestFile folder.LocalPath folder.FilePattern
        let storageUsed = getTotalSize folder.LocalPath folder.FilePattern

        {
            Name = folder.LocalName
            Description = sprintf "From %s:%s" host folder.RemotePath
            SourceType = LinuxServer host
            Path = Some folder.LocalPath
            LastBackup = latestBackup
            Staleness = calculateStaleness Thresholds.linuxServer latestBackup
            StorageUsedMB = storageUsed |> Option.map (fun s -> s / (1024L * 1024L))
            StorageTotalMB = None  // Linux folders don't have a quota
        }

    /// Scan all configured Linux folders
    let scanLinuxFolders (config: AppConfig) : BackupSource list =
        match config.LinuxServerHost with
        | Some host when not (System.String.IsNullOrWhiteSpace(host)) ->
            config.LinuxServerFolders
            |> List.map (scanLinuxFolder host)
        | _ -> []

    /// Scan all backup sources based on configuration
    let scanAll (config: AppConfig) : BackupSource list =
        [
            scanNotion config.NotionPath
            scanGoogleTakeout config.GoogleTakeoutPath
            // Scan all configured Linux folders
            yield! scanLinuxFolders config
        ]

    /// Format a DateTime as a relative time string
    let formatRelativeTime (date: DateTime option) : string =
        match date with
        | None -> "Never"
        | Some d ->
            let age = DateTime.Now - d
            if age.TotalMinutes < 1.0 then "Just now"
            elif age.TotalMinutes < 60.0 then sprintf "%.0f mins ago" age.TotalMinutes
            elif age.TotalHours < 24.0 then sprintf "%.0f hours ago" age.TotalHours
            elif age.TotalDays < 2.0 then "Yesterday"
            elif age.TotalDays < 7.0 then sprintf "%.0f days ago" age.TotalDays
            elif age.TotalDays < 30.0 then sprintf "%.0f weeks ago" (age.TotalDays / 7.0)
            elif age.TotalDays < 365.0 then sprintf "%.0f months ago" (age.TotalDays / 30.0)
            else sprintf "%.0f years ago" (age.TotalDays / 365.0)

    /// Format storage as a human-readable string
    let formatStorage (usedMB: int64 option) (totalMB: int64 option) : string =
        match usedMB, totalMB with
        | Some used, Some total ->
            let formatSize (mb: int64) =
                if mb >= 1024L then sprintf "%.1fGB" (float mb / 1024.0)
                else sprintf "%dMB" mb
            sprintf "%s / %s" (formatSize used) (formatSize total)
        | Some used, None ->
            let formatSize (mb: int64) =
                if mb >= 1024L then sprintf "%.1fGB" (float mb / 1024.0)
                else sprintf "%dMB" mb
            formatSize used
        | None, _ -> "Unknown"

    /// Calculate storage percentage
    let storagePercentage (usedMB: int64 option) (totalMB: int64 option) : float =
        match usedMB, totalMB with
        | Some used, Some total when total > 0L ->
            float used / float total * 100.0 |> min 100.0
        | _ -> 0.0
