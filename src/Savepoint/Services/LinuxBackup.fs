namespace Savepoint.Services

open System
open System.IO
open Savepoint.Domain

/// Service for downloading files from Linux server
module LinuxBackup =

    /// Status of an individual download operation
    type DownloadStatus =
        | NotStarted
        | InProgress of percent: int
        | Completed
        | Failed of error: string

    /// Progress information for a download operation
    type DownloadProgress = {
        FolderName: string
        CurrentFile: string
        CurrentFileIndex: int
        TotalFiles: int
        BytesDownloaded: int64
        TotalBytes: int64
        Status: DownloadStatus
    }

    /// Result of downloading a folder
    type FolderResult =
        | Success of filesDownloaded: int
        | PartialSuccess of downloaded: int * failed: int
        | Failed of error: string

    /// Create SSH credentials from config
    let private createCredentials (config: AppConfig) : SshConnection.SshCredentials option =
        match config.LinuxServerHost, config.LinuxServerUser, config.LinuxServerKeyPath with
        | Some host, Some user, Some keyPath when
            not (String.IsNullOrWhiteSpace(host)) &&
            not (String.IsNullOrWhiteSpace(user)) &&
            not (String.IsNullOrWhiteSpace(keyPath)) ->
            Some {
                Host = host
                Port = config.LinuxServerPort
                Username = user
                KeyPath = keyPath
                Passphrase = config.LinuxServerPassphrase
            }
        | _ -> None

    /// Download all files from a remote folder using optimized bulk transfer
    let downloadFolder
        (creds: SshConnection.SshCredentials)
        (folder: LinuxFolder)
        (onProgress: DownloadProgress -> unit)
        : Async<FolderResult> =
        async {
            try
                // Report starting - listing files
                onProgress {
                    FolderName = folder.LocalName
                    CurrentFile = "Scanning files..."
                    CurrentFileIndex = 0
                    TotalFiles = 0
                    BytesDownloaded = 0L
                    TotalBytes = 0L
                    Status = InProgress 0
                }

                // List remote files with sizes (for progress tracking)
                let! filesResult = SshConnection.listRemoteFilesWithSizes creds folder.RemotePath

                match filesResult with
                | Result.Error err ->
                    onProgress {
                        FolderName = folder.LocalName
                        CurrentFile = ""
                        CurrentFileIndex = 0
                        TotalFiles = 0
                        BytesDownloaded = 0L
                        TotalBytes = 0L
                        Status = DownloadStatus.Failed err
                    }
                    return FolderResult.Failed err

                | Result.Ok files when files.IsEmpty ->
                    onProgress {
                        FolderName = folder.LocalName
                        CurrentFile = ""
                        CurrentFileIndex = 0
                        TotalFiles = 0
                        BytesDownloaded = 0L
                        TotalBytes = 0L
                        Status = DownloadStatus.Completed
                    }
                    return FolderResult.Success 0

                | Result.Ok files ->
                    // Ensure base local directory exists
                    if not (Directory.Exists(folder.LocalPath)) then
                        Directory.CreateDirectory(folder.LocalPath) |> ignore

                    let totalBytes = files |> List.sumBy (fun f -> f.Size)
                    let totalFiles = files.Length

                    // Progress callback that converts bulk progress to our format
                    let bulkProgressCallback (p: SshConnection.BulkDownloadProgress) =
                        let percent =
                            if p.TotalBytes > 0L then
                                int ((p.BytesDownloadedTotal * 100L) / p.TotalBytes)
                            else
                                int ((p.CurrentFileIndex * 100) / p.TotalFiles)
                        onProgress {
                            FolderName = folder.LocalName
                            CurrentFile = p.CurrentFileName
                            CurrentFileIndex = p.CurrentFileIndex
                            TotalFiles = p.TotalFiles
                            BytesDownloaded = p.BytesDownloadedTotal
                            TotalBytes = p.TotalBytes
                            Status = InProgress percent
                        }

                    // Use bulk download for speed (single connection)
                    let! downloadResult =
                        SshConnection.downloadFilesBulk
                            creds
                            files
                            folder.LocalPath
                            bulkProgressCallback

                    match downloadResult with
                    | Result.Error err ->
                        onProgress {
                            FolderName = folder.LocalName
                            CurrentFile = ""
                            CurrentFileIndex = 0
                            TotalFiles = totalFiles
                            BytesDownloaded = 0L
                            TotalBytes = totalBytes
                            Status = DownloadStatus.Failed err
                        }
                        return FolderResult.Failed err

                    | Result.Ok (successCount, failCount) ->
                        // Report completion
                        onProgress {
                            FolderName = folder.LocalName
                            CurrentFile = ""
                            CurrentFileIndex = totalFiles
                            TotalFiles = totalFiles
                            BytesDownloaded = totalBytes
                            TotalBytes = totalBytes
                            Status = DownloadStatus.Completed
                        }

                        if failCount = 0 then
                            return FolderResult.Success successCount
                        elif successCount > 0 then
                            return FolderResult.PartialSuccess (successCount, failCount)
                        else
                            return FolderResult.Failed (sprintf "All %d files failed to download" failCount)

            with ex ->
                onProgress {
                    FolderName = folder.LocalName
                    CurrentFile = ""
                    CurrentFileIndex = 0
                    TotalFiles = 0
                    BytesDownloaded = 0L
                    TotalBytes = 0L
                    Status = DownloadStatus.Failed ex.Message
                }
                return FolderResult.Failed ex.Message
        }

    /// Download a single folder by name from config
    let downloadSingleFolder
        (config: AppConfig)
        (folderName: string)
        (onProgress: DownloadProgress -> unit)
        : Async<FolderResult> =
        async {
            match createCredentials config with
            | None ->
                return FolderResult.Failed "Linux server not configured"
            | Some creds ->
                match config.LinuxServerFolders |> List.tryFind (fun f -> f.LocalName = folderName) with
                | None ->
                    return FolderResult.Failed (sprintf "Folder '%s' not found in configuration" folderName)
                | Some folder ->
                    return! downloadFolder creds folder onProgress
        }

    /// Download all configured folders
    let downloadAll
        (config: AppConfig)
        (onProgress: DownloadProgress -> unit)
        : Async<(string * FolderResult) list> =
        async {
            match createCredentials config with
            | None ->
                return [ ("Linux Server", FolderResult.Failed "Linux server not configured") ]
            | Some creds ->
                let results = ResizeArray<string * FolderResult>()

                for folder in config.LinuxServerFolders do
                    let! result = downloadFolder creds folder onProgress
                    results.Add((folder.LocalName, result))

                return results |> Seq.toList
        }

    /// Format a folder result for display
    let formatResult (result: FolderResult) : string =
        match result with
        | Success count ->
            if count = 0 then "No files to download"
            else sprintf "Downloaded %d file(s)" count
        | PartialSuccess (downloaded, failed) ->
            sprintf "Downloaded %d, failed %d" downloaded failed
        | Failed error ->
            sprintf "Failed: %s" error

    /// Check if a result indicates success (full or partial)
    let isSuccess (result: FolderResult) : bool =
        match result with
        | Success _ -> true
        | PartialSuccess _ -> true
        | Failed _ -> false
