namespace Savepoint.Views

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Savepoint
open Savepoint.Domain
open Savepoint.Services
open Savepoint.Views.Components

/// Dashboard view showing backup source overview
module Dashboard =

    type LinuxServerStatus =
        | NotConfigured
        | Configured of folderCount: int

    type State = {
        Sources: BackupSource list
        LastRefresh: DateTime
        LinuxServerStatus: LinuxServerStatus
        DownloadState: DownloadState
        // VeraCrypt state
        VeraCryptStatus: VeraCryptStatus
        PasswordDialog: Components.PasswordDialog.State
        // Sync state
        SyncState: SyncState
        SyncOperations: FileEntry list    // Structured file operations
        ExpandedDirs: Set<string>         // Track expanded directories
        IsLogExpanded: bool
        SyncTreeRootLabel: string option  // Root label for sync tree (e.g., "G Drive")
    }

    type Msg =
        | Refresh
        | RefreshComplete of BackupSource list
        | StartLinuxDownload of folderName: string
        | DownloadProgressUpdate of DownloadProgressInfo
        | DownloadComplete of DownloadResult list
        | DownloadError of string
        | DismissResults
        // VeraCrypt messages
        | RefreshVeraCryptStatus
        | OpenPasswordDialog
        | SetPassword of string
        | MountVolume
        | MountResult of Result<char, string>
        | DismountVolume
        | DismountResult of Result<unit, string>
        | ClosePasswordDialog
        // Sync messages
        | StartSync of isDryRun: bool
        | SyncProgressUpdate of RobocopyProgressInfo
        | SyncFileOperation of FileEntry
        | SyncFileOperationsBatch of FileEntry list
        | SyncComplete of Result<RobocopySummary, string>
        | DismissSyncResults
        | ToggleLogPanel
        | ToggleDirectory of path: string
        | ExpandAllDirs
        | CollapseAllDirs

    let private getLinuxServerStatus (config: AppConfig) =
        match config.LinuxServerHost with
        | Some host when not (String.IsNullOrWhiteSpace(host)) ->
            Configured config.LinuxServerFolders.Length
        | _ -> NotConfigured

    let private getVeraCryptStatus (config: AppConfig) =
        match config.VeraCrypt with
        | None -> VCNotConfigured
        | Some vc -> VeraCrypt.getStatus vc

    let init (config: AppConfig) =
        { Sources = Staleness.scanAll config
          LastRefresh = DateTime.Now
          LinuxServerStatus = getLinuxServerStatus config
          DownloadState = Idle
          VeraCryptStatus = getVeraCryptStatus config
          PasswordDialog = Components.PasswordDialog.empty
          SyncState = SyncIdle
          SyncOperations = []
          ExpandedDirs = Set.empty
          IsLogExpanded = false
          SyncTreeRootLabel = None }

    /// Convert LinuxBackup.FolderResult to Domain.DownloadResult
    let private toDownloadResult (folderName: string) (result: LinuxBackup.FolderResult) : DownloadResult =
        match result with
        | LinuxBackup.FolderResult.Success count ->
            { FolderName = folderName; FilesDownloaded = count; FilesFailed = 0; ErrorMessage = None }
        | LinuxBackup.FolderResult.PartialSuccess (downloaded, failed) ->
            { FolderName = folderName; FilesDownloaded = downloaded; FilesFailed = failed; ErrorMessage = None }
        | LinuxBackup.FolderResult.Failed error ->
            { FolderName = folderName; FilesDownloaded = 0; FilesFailed = 0; ErrorMessage = Some error }

    /// Create the async command to download a folder (runs on background thread)
    let private downloadFolderCmd (config: AppConfig) (folderName: string) : Elmish.Cmd<Msg> =
        let download dispatch =
            async {
                let onProgress (progress: LinuxBackup.DownloadProgress) =
                    match progress.Status with
                    | LinuxBackup.DownloadStatus.InProgress percent ->
                        // Dispatch to UI thread
                        let progressInfo: DownloadProgressInfo = {
                            FolderName = progress.FolderName
                            CurrentFile = progress.CurrentFile
                            CurrentFileIndex = progress.CurrentFileIndex
                            TotalFiles = progress.TotalFiles
                            BytesDownloaded = progress.BytesDownloaded
                            TotalBytes = progress.TotalBytes
                            Percent = percent
                        }
                        Dispatcher.UIThread.Post(fun () ->
                            dispatch (DownloadProgressUpdate progressInfo))
                    | _ -> ()

                let! result = LinuxBackup.downloadSingleFolder config folderName onProgress
                let downloadResult = toDownloadResult folderName result
                // Dispatch completion to UI thread
                Dispatcher.UIThread.Post(fun () ->
                    dispatch (DownloadComplete [downloadResult]))
            }
            |> Async.Start  // Run on thread pool, not UI thread
        Elmish.Cmd.ofEffect download

    /// Create the async command to mount a VeraCrypt volume
    let private mountVolumeCmd (config: AppConfig) (password: string) : Elmish.Cmd<Msg> =
        let mount dispatch =
            async {
                match config.VeraCrypt with
                | None -> Dispatcher.UIThread.Post(fun () -> dispatch (MountResult (Result.Error "VeraCrypt not configured")))
                | Some vc ->
                    let! result = VeraCrypt.mount vc password
                    Dispatcher.UIThread.Post(fun () -> dispatch (MountResult result))
            }
            |> Async.Start
        Elmish.Cmd.ofEffect mount

    /// Create the async command to dismount a VeraCrypt volume
    let private dismountVolumeCmd (config: AppConfig) : Elmish.Cmd<Msg> =
        let dismount dispatch =
            async {
                match config.VeraCrypt with
                | None -> Dispatcher.UIThread.Post(fun () -> dispatch (DismountResult (Result.Error "VeraCrypt not configured")))
                | Some vc ->
                    let! result = VeraCrypt.dismount vc
                    Dispatcher.UIThread.Post(fun () -> dispatch (DismountResult result))
            }
            |> Async.Start
        Elmish.Cmd.ofEffect dismount

    /// Create the async command to run Robocopy sync with batched UI updates
    let private syncCmd (config: AppConfig) (isDryRun: bool) : Elmish.Cmd<Msg> =
        let sync dispatch =
            async {
                match config.Robocopy with
                | None -> Dispatcher.UIThread.Post(fun () -> dispatch (SyncComplete (Result.Error "Robocopy not configured")))
                | Some rc ->
                    // Batch file operations to avoid UI freezing
                    let pendingEntries = System.Collections.Generic.List<FileEntry>()
                    let batchLock = obj()
                    let mutable lastFlush = DateTime.Now

                    let flushBatch () =
                        lock batchLock (fun () ->
                            if pendingEntries.Count > 0 then
                                let batch = pendingEntries |> Seq.toList
                                pendingEntries.Clear()
                                lastFlush <- DateTime.Now
                                Dispatcher.UIThread.Post(fun () -> dispatch (SyncFileOperationsBatch batch))
                        )

                    let onProgress progress =
                        Dispatcher.UIThread.Post(fun () -> dispatch (SyncProgressUpdate progress))

                    let onFileOperation fileEntry =
                        lock batchLock (fun () ->
                            pendingEntries.Add(fileEntry)
                            // Flush every 50 entries or every 200ms
                            let timeSinceLastFlush = (DateTime.Now - lastFlush).TotalMilliseconds
                            if pendingEntries.Count >= 50 || timeSinceLastFlush >= 200.0 then
                                let batch = pendingEntries |> Seq.toList
                                pendingEntries.Clear()
                                lastFlush <- DateTime.Now
                                Dispatcher.UIThread.Post(fun () -> dispatch (SyncFileOperationsBatch batch))
                        )

                    let! result = Robocopy.runSync rc isDryRun onProgress onFileOperation
                    // Flush any remaining entries
                    flushBatch()
                    Dispatcher.UIThread.Post(fun () -> dispatch (SyncComplete result))
            }
            |> Async.Start
        Elmish.Cmd.ofEffect sync

    let private stripSyncPrefix (config: AppConfig) (entry: FileEntry) : FileEntry =
        match config.Robocopy with
        | Some rc ->
            let srcPrefix = rc.SourcePath.TrimEnd('\\', '/') + "\\"
            let dstPrefix = rc.DestinationPath.TrimEnd('\\', '/') + "\\"
            let stripped =
                if entry.FullPath.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase) then
                    entry.FullPath.Substring(srcPrefix.Length)
                elif entry.FullPath.StartsWith(dstPrefix, StringComparison.OrdinalIgnoreCase) then
                    entry.FullPath.Substring(dstPrefix.Length)
                else
                    entry.FullPath
            { entry with FullPath = stripped; FileName = Path.GetFileName(stripped) }
        | None -> entry

    let update (config: AppConfig) (msg: Msg) (state: State) : State * Elmish.Cmd<Msg> =
        match msg with
        | Refresh ->
            { state with
                Sources = Staleness.scanAll config
                LastRefresh = DateTime.Now
                LinuxServerStatus = getLinuxServerStatus config
                VeraCryptStatus = getVeraCryptStatus config }, Elmish.Cmd.none
        | RefreshComplete sources ->
            { state with Sources = sources; LastRefresh = DateTime.Now }, Elmish.Cmd.none
        | StartLinuxDownload folderName ->
            let initialProgress: DownloadProgressInfo = {
                FolderName = folderName
                CurrentFile = "Starting..."
                CurrentFileIndex = 0
                TotalFiles = 0
                BytesDownloaded = 0L
                TotalBytes = 0L
                Percent = 0
            }
            { state with DownloadState = Downloading initialProgress },
            downloadFolderCmd config folderName
        | DownloadProgressUpdate progressInfo ->
            { state with DownloadState = Downloading progressInfo }, Elmish.Cmd.none
        | DownloadComplete results ->
            { state with
                DownloadState = Completed results
                Sources = Staleness.scanAll config
                LastRefresh = DateTime.Now }, Elmish.Cmd.none
        | DownloadError message ->
            { state with DownloadState = Error message }, Elmish.Cmd.none
        | DismissResults ->
            { state with DownloadState = Idle }, Elmish.Cmd.none

        // VeraCrypt messages
        | RefreshVeraCryptStatus ->
            { state with VeraCryptStatus = getVeraCryptStatus config }, Elmish.Cmd.none

        | OpenPasswordDialog ->
            let partitionName = config.VeraCrypt |> Option.bind (fun vc -> vc.PartitionDevicePath) |> Option.defaultValue ""
            { state with PasswordDialog = Components.PasswordDialog.open' partitionName }, Elmish.Cmd.none

        | SetPassword password ->
            { state with PasswordDialog = { state.PasswordDialog with Password = password } }, Elmish.Cmd.none

        | MountVolume ->
            { state with PasswordDialog = Components.PasswordDialog.setLoading state.PasswordDialog },
            mountVolumeCmd config state.PasswordDialog.Password

        | MountResult result ->
            match result with
            | Result.Ok driveLetter ->
                { state with
                    PasswordDialog = Components.PasswordDialog.empty
                    VeraCryptStatus = Mounted driveLetter }, Elmish.Cmd.none
            | Result.Error err ->
                { state with PasswordDialog = Components.PasswordDialog.setError err state.PasswordDialog }, Elmish.Cmd.none

        | DismountVolume ->
            state, dismountVolumeCmd config

        | DismountResult result ->
            match result with
            | Result.Ok () ->
                { state with VeraCryptStatus = Unmounted }, Elmish.Cmd.none
            | Result.Error err ->
                { state with VeraCryptStatus = VeraCryptError err }, Elmish.Cmd.none

        | ClosePasswordDialog ->
            { state with PasswordDialog = Components.PasswordDialog.empty }, Elmish.Cmd.none

        // Sync messages
        | StartSync isDryRun ->
            let initialProgress = {
                CurrentFile = "Starting..."
                FilesProcessed = 0
                OverallPercent = 0
                IsDryRun = isDryRun
            }
            let syncState = if isDryRun then DryRunning initialProgress else Syncing initialProgress
            let rootLabel =
                match config.Robocopy with
                | Some rc ->
                    let src = rc.SourcePath.TrimEnd('\\', '/')
                    if src.Length >= 2 && src.[1] = ':' then
                        // Drive letter path like "G:\My Drive" -> "G Drive"
                        sprintf "%c Drive" src.[0]
                    elif src.StartsWith("\\\\") then
                        // UNC path -> use last segment
                        let parts = src.Split([|'\\'; '/'|], System.StringSplitOptions.RemoveEmptyEntries)
                        if parts.Length > 0 then parts.[parts.Length - 1] else src
                    else src
                | None -> "Sync"
            { state with SyncState = syncState; SyncOperations = []; ExpandedDirs = Set.singleton "__root__"; SyncTreeRootLabel = Some rootLabel }, syncCmd config isDryRun

        | SyncProgressUpdate progress ->
            let syncState =
                if progress.IsDryRun then DryRunning progress else Syncing progress
            { state with SyncState = syncState }, Elmish.Cmd.none

        | SyncFileOperation fileEntry ->
            let stripped = stripSyncPrefix config fileEntry
            { state with SyncOperations = stripped :: state.SyncOperations }, Elmish.Cmd.none

        | SyncFileOperationsBatch entries ->
            let stripped = entries |> List.map (stripSyncPrefix config)
            { state with SyncOperations = stripped @ state.SyncOperations }, Elmish.Cmd.none

        | SyncComplete result ->
            match result with
            | Result.Ok summary ->
                { state with SyncState = SyncCompleted summary }, Elmish.Cmd.none
            | Result.Error err ->
                { state with SyncState = SyncError err }, Elmish.Cmd.none

        | DismissSyncResults ->
            { state with SyncState = SyncIdle; SyncOperations = []; ExpandedDirs = Set.empty; SyncTreeRootLabel = None }, Elmish.Cmd.none

        | ToggleLogPanel ->
            { state with IsLogExpanded = not state.IsLogExpanded }, Elmish.Cmd.none

        | ToggleDirectory path ->
            let newExpanded =
                if state.ExpandedDirs.Contains(path) then
                    state.ExpandedDirs.Remove(path)
                else
                    state.ExpandedDirs.Add(path)
            { state with ExpandedDirs = newExpanded }, Elmish.Cmd.none

        | ExpandAllDirs ->
            // Collect all directory paths from operations
            let allDirs =
                state.SyncOperations
                |> List.collect (fun entry ->
                    try
                        let dir = Path.GetDirectoryName(entry.FullPath)
                        if String.IsNullOrEmpty(dir) then []
                        else
                            // Get all parent directories
                            let rec getAllParents (path: string) acc =
                                if String.IsNullOrEmpty(path) then acc
                                else
                                    let parent = Path.GetDirectoryName(path)
                                    getAllParents parent (path :: acc)
                            getAllParents dir []
                    with _ -> [])
                |> Set.ofList
            let allDirs =
                match state.SyncTreeRootLabel with
                | Some _ -> allDirs.Add("__root__")
                | None -> allDirs
            { state with ExpandedDirs = allDirs }, Elmish.Cmd.none

        | CollapseAllDirs ->
            { state with ExpandedDirs = Set.empty }, Elmish.Cmd.none

    /// Create a system metrics card
    let private createMetricCard (icon: string) (iconColor: IBrush) (label: string) (value: string) (rightContent: IView option) =
        Border.create [
            Border.cornerRadius 12.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 153, Theme.surface.R, Theme.surface.G, Theme.surface.B)))
            Border.padding 16.0
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        // Right content (optional)
                        match rightContent with
                        | Some content ->
                            Border.create [
                                DockPanel.dock Dock.Right
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.child content
                            ]
                        | None -> ()

                        // Left content: icon + text
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 16.0
                            StackPanel.children [
                                // Icon
                                Border.create [
                                    Border.padding 12.0
                                    Border.cornerRadius 8.0
                                    Border.background (
                                        let color = (iconColor :?> SolidColorBrush).Color
                                        SolidColorBrush(Color.FromArgb(byte 25, color.R, color.G, color.B))
                                    )
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text icon
                                            TextBlock.foreground iconColor
                                            TextBlock.fontSize 20.0
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                                // Text
                                StackPanel.create [
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text label
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                            TextBlock.fontWeight FontWeight.Medium
                                        ]
                                        TextBlock.create [
                                            TextBlock.text value
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeLg
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// Create a health badge
    let private createHealthBadge (text: string) (color: IBrush) =
        Border.create [
            Border.padding (Thickness(8.0, 4.0, 8.0, 4.0))
            Border.cornerRadius 4.0
            Border.background (
                let c = (color :?> SolidColorBrush).Color
                SolidColorBrush(Color.FromArgb(byte 25, c.R, c.G, c.B))
            )
            Border.child (
                TextBlock.create [
                    TextBlock.text text
                    TextBlock.foreground color
                    TextBlock.fontSize Theme.Typography.fontSizeXs
                    TextBlock.fontWeight FontWeight.Medium
                ]
            )
        ] :> IView

    /// Create the recent activity placeholder
    let private createRecentActivityPlaceholder () =
        Border.create [
            Border.cornerRadius Theme.Sizes.cardRadius
            Border.background Theme.Brushes.surface
            Border.borderBrush Theme.Brushes.transparent
            Border.borderThickness 0.0
            Border.child (
                StackPanel.create [
                    StackPanel.children [
                        // Header
                        DockPanel.create [
                            DockPanel.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                            DockPanel.children [
                                Border.create [
                                    Border.padding 16.0
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text "Recent Activity"
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeLg
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                            ]
                        ]
                        // Placeholder content
                        Border.create [
                            Border.padding (Thickness(16.0, 32.0, 16.0, 32.0))
                            Border.child (
                                StackPanel.create [
                                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "No recent activity"
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeMd
                                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        ]
                                        TextBlock.create [
                                            TextBlock.text "Activity logs will appear here after running backups."
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                                            TextBlock.margin (Thickness(0.0, 8.0, 0.0, 0.0))
                                        ]
                                    ]
                                ]
                            )
                        ]
                    ]
                ]
            )
        ]

    /// Format bytes as human readable
    let private formatBytes (bytes: int64) =
        if bytes < 1024L then sprintf "%d B" bytes
        elif bytes < 1024L * 1024L then sprintf "%.1f KB" (float bytes / 1024.0)
        elif bytes < 1024L * 1024L * 1024L then sprintf "%.1f MB" (float bytes / 1024.0 / 1024.0)
        else sprintf "%.2f GB" (float bytes / 1024.0 / 1024.0 / 1024.0)

    /// Create download progress overlay with detailed info
    let private createDownloadProgress (progress: DownloadProgressInfo) =
        let statusText =
            if progress.TotalFiles > 0 then
                sprintf "File %d of %d" progress.CurrentFileIndex progress.TotalFiles
            else
                "Scanning files..."

        let sizeText =
            if progress.TotalBytes > 0L then
                sprintf "%s / %s" (formatBytes progress.BytesDownloaded) (formatBytes progress.TotalBytes)
            else
                ""

        Border.create [
            Border.cornerRadius 12.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 230, Theme.surface.R, Theme.surface.G, Theme.surface.B)))
            Border.padding 20.0
            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 12.0
                    StackPanel.children [
                        // Header row
                        DockPanel.create [
                            DockPanel.children [
                                TextBlock.create [
                                    DockPanel.dock Dock.Right
                                    TextBlock.text (sprintf "%d%%" progress.Percent)
                                    TextBlock.foreground Theme.Brushes.primary
                                    TextBlock.fontSize Theme.Typography.fontSizeLg
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                StackPanel.create [
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "Downloading from Linux Server"
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeMd
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                        TextBlock.create [
                                            TextBlock.text progress.FolderName
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                        ]
                                    ]
                                ]
                            ]
                        ]
                        // Progress bar
                        ProgressBar.viewSimple (float progress.Percent) 0.0
                        // Details row
                        DockPanel.create [
                            DockPanel.children [
                                TextBlock.create [
                                    DockPanel.dock Dock.Right
                                    TextBlock.text sizeText
                                    TextBlock.foreground Theme.Brushes.textMuted
                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                ]
                                TextBlock.create [
                                    TextBlock.text statusText
                                    TextBlock.foreground Theme.Brushes.textSecondary
                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                ]
                            ]
                        ]
                        // Current file
                        if not (String.IsNullOrEmpty(progress.CurrentFile)) then
                            TextBlock.create [
                                TextBlock.text progress.CurrentFile
                                TextBlock.foreground Theme.Brushes.textMuted
                                TextBlock.fontSize Theme.Typography.fontSizeXs
                                TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                TextBlock.maxWidth 500.0
                            ]
                    ]
                ]
            )
        ]

    /// Create download results banner
    let private createResultsBanner (results: DownloadResult list) (dispatch: Msg -> unit) =
        let totalDownloaded = results |> List.sumBy (fun r -> r.FilesDownloaded)
        let totalFailed = results |> List.sumBy (fun r -> r.FilesFailed)
        let hasErrors = totalFailed > 0 || results |> List.exists (fun r -> r.ErrorMessage.IsSome)

        let (statusText, statusColor) =
            if hasErrors then ("Completed with errors", Theme.Brushes.accentRed :> IBrush)
            else ("Download complete", Theme.Brushes.accentGreen :> IBrush)

        Border.create [
            Border.cornerRadius 12.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 230, Theme.surface.R, Theme.surface.G, Theme.surface.B)))
            Border.padding 20.0
            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        Button.create [
                            DockPanel.dock Dock.Right
                            Button.content "Dismiss"
                            Button.background Theme.Brushes.transparent
                            Button.foreground Theme.Brushes.textMuted
                            Button.fontSize Theme.Typography.fontSizeSm
                            Button.padding (Thickness(8.0, 4.0, 8.0, 4.0))
                            Button.onClick (fun _ -> dispatch DismissResults)
                        ]
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text statusText
                                    TextBlock.foreground statusColor
                                    TextBlock.fontSize Theme.Typography.fontSizeMd
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                TextBlock.create [
                                    TextBlock.text (
                                        if hasErrors then
                                            sprintf "Downloaded %d file(s), %d failed" totalDownloaded totalFailed
                                        else
                                            sprintf "Downloaded %d file(s)" totalDownloaded
                                    )
                                    TextBlock.foreground Theme.Brushes.textMuted
                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// Create error banner
    let private createErrorBanner (message: string) (dispatch: Msg -> unit) =
        Border.create [
            Border.cornerRadius 12.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 25, byte 255, byte 100, byte 100)))
            Border.padding 20.0
            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        Button.create [
                            DockPanel.dock Dock.Right
                            Button.content "Dismiss"
                            Button.background Theme.Brushes.transparent
                            Button.foreground Theme.Brushes.textMuted
                            Button.fontSize Theme.Typography.fontSizeSm
                            Button.padding (Thickness(8.0, 4.0, 8.0, 4.0))
                            Button.onClick (fun _ -> dispatch DismissResults)
                        ]
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text "Download failed"
                                    TextBlock.foreground Theme.Brushes.accentRed
                                    TextBlock.fontSize Theme.Typography.fontSizeMd
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                TextBlock.create [
                                    TextBlock.text message
                                    TextBlock.foreground Theme.Brushes.textMuted
                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// Calculate overall system health based on sources
    let private getSystemHealth (sources: BackupSource list) =
        let hasCritical = sources |> List.exists (fun s -> s.Staleness = Critical)
        let hasStale = sources |> List.exists (fun s -> s.Staleness = Stale)
        let hasUnknown = sources |> List.exists (fun s -> s.Staleness = Unknown)

        if hasCritical then ("Critical", Theme.Brushes.accentRed :> IBrush)
        elif hasStale then ("Attention", SolidColorBrush(Theme.accentYellow) :> IBrush)
        elif hasUnknown then ("Unknown", Theme.Brushes.textMuted :> IBrush)
        else ("Healthy", Theme.Brushes.accentGreen :> IBrush)

    /// Create VeraCrypt status card
    let private createVeraCryptCard (status: VeraCryptStatus) (dispatch: Msg -> unit) =
        Border.create [
            Border.cornerRadius 12.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 153, Theme.surface.R, Theme.surface.G, Theme.surface.B)))
            Border.padding 20.0
            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        // Buttons (right)
                        StackPanel.create [
                            DockPanel.dock Dock.Right
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.children [
                                match status with
                                | VCNotConfigured ->
                                    TextBlock.create [
                                        TextBlock.text "Configure in Settings"
                                        TextBlock.foreground Theme.Brushes.textMuted
                                        TextBlock.fontSize Theme.Typography.fontSizeSm
                                        TextBlock.fontStyle FontStyle.Italic
                                    ]
                                | Unmounted ->
                                    Button.create [
                                        Button.content "Mount"
                                        Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                        Button.background Theme.Brushes.primary
                                        Button.foreground (SolidColorBrush(Colors.White))
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.fontWeight FontWeight.Bold
                                        Button.cornerRadius 6.0
                                        Button.onClick (fun _ -> dispatch OpenPasswordDialog)
                                    ]
                                | Mounted _ ->
                                    Button.create [
                                        Button.content "Dismount"
                                        Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                        Button.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.secondary.R, Theme.secondary.G, Theme.secondary.B)))
                                        Button.foreground Theme.Brushes.secondary
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.fontWeight FontWeight.Medium
                                        Button.cornerRadius 6.0
                                        Button.onClick (fun _ -> dispatch DismountVolume)
                                    ]
                                | VCUnknown ->
                                    TextBlock.create [
                                        TextBlock.text "Unknown status"
                                        TextBlock.foreground Theme.Brushes.textMuted
                                        TextBlock.fontSize Theme.Typography.fontSizeSm
                                    ]
                                | VeraCryptError _ ->
                                    Button.create [
                                        Button.content "Retry"
                                        Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                        Button.background Theme.Brushes.transparent
                                        Button.foreground Theme.Brushes.textMuted
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.cornerRadius 6.0
                                        Button.onClick (fun _ -> dispatch RefreshVeraCryptStatus)
                                    ]
                            ]
                        ]

                        // Left content: icon + text
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 16.0
                            StackPanel.children [
                                // Icon
                                Border.create [
                                    Border.padding 12.0
                                    Border.cornerRadius 8.0
                                    Border.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.secondary.R, Theme.secondary.G, Theme.secondary.B)))
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text "V"
                                            TextBlock.foreground Theme.Brushes.secondary
                                            TextBlock.fontSize 20.0
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                                // Text
                                StackPanel.create [
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "VERACRYPT VOLUME"
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                            TextBlock.fontWeight FontWeight.Medium
                                        ]
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 8.0
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text (
                                                        match status with
                                                        | VCNotConfigured -> "Not Configured"
                                                        | Unmounted -> "Unmounted"
                                                        | Mounted letter -> sprintf "Mounted (%c:)" letter
                                                        | VCUnknown -> "Unknown"
                                                        | VeraCryptError _ -> "Error"
                                                    )
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeLg
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                                // Status indicator
                                                Border.create [
                                                    Border.width 8.0
                                                    Border.height 8.0
                                                    Border.cornerRadius 4.0
                                                    Border.verticalAlignment VerticalAlignment.Center
                                                    Border.background (
                                                        match status with
                                                        | Mounted _ -> Theme.Brushes.accentGreen
                                                        | Unmounted -> Theme.Brushes.textMuted
                                                        | VeraCryptError _ -> Theme.Brushes.accentRed
                                                        | _ -> Theme.Brushes.textMuted
                                                    )
                                                ]
                                            ]
                                        ]
                                        match status with
                                        | VeraCryptError msg ->
                                            TextBlock.create [
                                                TextBlock.text msg
                                                TextBlock.foreground Theme.Brushes.accentRed
                                                TextBlock.fontSize Theme.Typography.fontSizeXs
                                                TextBlock.textWrapping TextWrapping.Wrap
                                                TextBlock.maxWidth 300.0
                                            ]
                                        | _ -> ()
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// Create sync control panel
    let private createSyncControlPanel (state: State) (dispatch: Msg -> unit) =
        let isMounted = match state.VeraCryptStatus with | Mounted _ -> true | _ -> false
        let isIdle = match state.SyncState with | SyncIdle -> true | _ -> false
        let canSync = isMounted && isIdle

        Border.create [
            Border.cornerRadius 12.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 153, Theme.surface.R, Theme.surface.G, Theme.surface.B)))
            Border.padding 20.0
            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 16.0
                    StackPanel.children [
                        // Header row
                        DockPanel.create [
                            DockPanel.children [
                                // Buttons (right)
                                StackPanel.create [
                                    DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children [
                                        Button.create [
                                            Button.content "Dry Run"
                                            Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                            Button.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.secondary.R, Theme.secondary.G, Theme.secondary.B)))
                                            Button.foreground Theme.Brushes.secondary
                                            Button.fontSize Theme.Typography.fontSizeSm
                                            Button.fontWeight FontWeight.Medium
                                            Button.cornerRadius 6.0
                                            Button.isEnabled canSync
                                            Button.onClick (fun _ -> dispatch (StartSync true))
                                        ]
                                        Button.create [
                                            Button.content "Run Backup"
                                            Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                            Button.background Theme.Brushes.primary
                                            Button.foreground (SolidColorBrush(Colors.White))
                                            Button.fontSize Theme.Typography.fontSizeSm
                                            Button.fontWeight FontWeight.Bold
                                            Button.cornerRadius 6.0
                                            Button.isEnabled canSync
                                            Button.onClick (fun _ -> dispatch (StartSync false))
                                        ]
                                    ]
                                ]

                                // Title (left)
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 16.0
                                    StackPanel.children [
                                        Border.create [
                                            Border.padding 12.0
                                            Border.cornerRadius 8.0
                                            Border.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.primary.R, Theme.primary.G, Theme.primary.B)))
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text "B"
                                                    TextBlock.foreground Theme.Brushes.primary
                                                    TextBlock.fontSize 20.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                            )
                                        ]
                                        StackPanel.create [
                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text "FILE BACKUP (ROBOCOPY)"
                                                    TextBlock.foreground Theme.Brushes.textMuted
                                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                                    TextBlock.fontWeight FontWeight.Medium
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text (
                                                        match state.SyncState with
                                                        | SyncIdle -> if isMounted then "Ready" else "Mount volume first"
                                                        | Syncing _ -> "Syncing..."
                                                        | DryRunning _ -> "Dry Run..."
                                                        | SyncCompleted _ -> "Complete"
                                                        | SyncError _ -> "Error"
                                                    )
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeLg
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]

                        // Progress section
                        match state.SyncState with
                        | Syncing progress | DryRunning progress ->
                            StackPanel.create [
                                StackPanel.spacing 8.0
                                StackPanel.children [
                                    ProgressBar.viewSimple (float progress.OverallPercent) 0.0
                                    DockPanel.create [
                                        DockPanel.children [
                                            TextBlock.create [
                                                DockPanel.dock Dock.Right
                                                TextBlock.text (sprintf "%d files" progress.FilesProcessed)
                                                TextBlock.foreground Theme.Brushes.textMuted
                                                TextBlock.fontSize Theme.Typography.fontSizeXs
                                            ]
                                            TextBlock.create [
                                                TextBlock.text progress.CurrentFile
                                                TextBlock.foreground Theme.Brushes.textSecondary
                                                TextBlock.fontSize Theme.Typography.fontSizeXs
                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                                TextBlock.maxWidth 400.0
                                            ]
                                        ]
                                    ]
                                ]
                            ]

                        | SyncCompleted summary ->
                            DockPanel.create [
                                DockPanel.children [
                                    Button.create [
                                        DockPanel.dock Dock.Right
                                        Button.content "Dismiss"
                                        Button.background Theme.Brushes.transparent
                                        Button.foreground Theme.Brushes.textMuted
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.padding (Thickness(8.0, 4.0, 8.0, 4.0))
                                        Button.onClick (fun _ -> dispatch DismissSyncResults)
                                    ]
                                    StackPanel.create [
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 16.0
                                        StackPanel.children [
                                            TextBlock.create [
                                                TextBlock.text (sprintf "Copied: %d" summary.FilesCopied)
                                                TextBlock.foreground Theme.Brushes.accentGreen
                                                TextBlock.fontSize Theme.Typography.fontSizeSm
                                                TextBlock.fontWeight FontWeight.Medium
                                            ]
                                            TextBlock.create [
                                                TextBlock.text (sprintf "Skipped: %d" summary.FilesSkipped)
                                                TextBlock.foreground Theme.Brushes.textMuted
                                                TextBlock.fontSize Theme.Typography.fontSizeSm
                                            ]
                                            if summary.FilesFailed > 0 then
                                                TextBlock.create [
                                                    TextBlock.text (sprintf "Failed: %d" summary.FilesFailed)
                                                    TextBlock.foreground Theme.Brushes.accentRed
                                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                                    TextBlock.fontWeight FontWeight.Medium
                                                ]
                                            TextBlock.create [
                                                TextBlock.text (sprintf "(%s)" (formatBytes summary.BytesCopied))
                                                TextBlock.foreground Theme.Brushes.textMuted
                                                TextBlock.fontSize Theme.Typography.fontSizeSm
                                            ]
                                        ]
                                    ]
                                ]
                            ]

                        | SyncError err ->
                            DockPanel.create [
                                DockPanel.children [
                                    Button.create [
                                        DockPanel.dock Dock.Right
                                        Button.content "Dismiss"
                                        Button.background Theme.Brushes.transparent
                                        Button.foreground Theme.Brushes.textMuted
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.padding (Thickness(8.0, 4.0, 8.0, 4.0))
                                        Button.onClick (fun _ -> dispatch DismissSyncResults)
                                    ]
                                    TextBlock.create [
                                        TextBlock.text err
                                        TextBlock.foreground Theme.Brushes.accentRed
                                        TextBlock.fontSize Theme.Typography.fontSizeSm
                                        TextBlock.textWrapping TextWrapping.Wrap
                                    ]
                                ]
                            ]

                        | SyncIdle -> ()
                    ]
                ]
            )
        ]

    /// Module for building sync tree from file entries
    module SyncTree =
        /// Safely get directory name
        let private safeGetDirName (path: string) =
            try
                let dir = Path.GetDirectoryName(path)
                if String.IsNullOrEmpty(dir) then None else Some dir
            with _ -> None

        /// Get all ancestor directories of a path (including the path itself)
        let private getAllAncestors (path: string) : string list =
            let rec loop current acc =
                if String.IsNullOrEmpty(current) then acc
                else
                    match safeGetDirName current with
                    | Some parent when parent <> current -> loop parent (current :: acc)
                    | _ -> current :: acc
            loop path []

        /// Build a tree structure from file entries with proper nested directories
        let buildTree (rootLabel: string option) (entries: FileEntry list) : SyncTreeNode list =
            if entries.IsEmpty then []
            else
                // Group files by their immediate directory
                let filesByDir =
                    entries
                    |> List.choose (fun e ->
                        match safeGetDirName e.FullPath with
                        | Some dir -> Some (dir, e)
                        | None -> None)
                    |> List.groupBy fst
                    |> Map.ofList
                    |> Map.map (fun _ items -> items |> List.map snd)

                // Collect all directory paths (including intermediate ones)
                let allDirPaths =
                    filesByDir
                    |> Map.toList
                    |> List.collect (fun (dir, _) -> getAllAncestors dir)
                    |> Set.ofList

                // Find the deepest common ancestor directory
                let findCommonRoot (paths: string list) =
                    match paths with
                    | [] -> None
                    | first :: rest ->
                        let ancestors = getAllAncestors first
                        // Search from deepest to shallowest to find the deepest common ancestor
                        ancestors
                        |> List.rev
                        |> List.tryFind (fun ancestor ->
                            rest |> List.forall (fun p -> p.StartsWith(ancestor + "\\") || p = ancestor))

                let dirList = allDirPaths |> Set.toList
                let commonRoot = findCommonRoot dirList

                // Build tree recursively
                let rec buildNode (dirPath: string) : SyncTreeNode =
                    let dirName = Path.GetFileName(dirPath)
                    let displayName = if String.IsNullOrEmpty(dirName) then dirPath else dirName

                    // Get files directly in this directory
                    let files =
                        filesByDir
                        |> Map.tryFind dirPath
                        |> Option.defaultValue []
                        |> List.map FileNode

                    // Get child directories (directories whose parent is this directory)
                    let childDirs =
                        allDirPaths
                        |> Set.filter (fun path ->
                            match safeGetDirName path with
                            | Some parent -> parent = dirPath && path <> dirPath
                            | None -> false)
                        |> Set.toList
                        |> List.map buildNode

                    DirectoryNode(displayName, dirPath, childDirs @ files)

                // Return the common root as the single root node
                let internalRoots =
                    match commonRoot with
                    | Some root ->
                        // Use the common root as the single tree root
                        [ buildNode root ]
                    | None ->
                        // No common root found, find directories whose parent is not in the set
                        let rootDirs =
                            allDirPaths
                            |> Set.filter (fun dir ->
                                match safeGetDirName dir with
                                | Some parent -> not (allDirPaths.Contains(parent))
                                | None -> true)
                            |> Set.toList
                        rootDirs |> List.map buildNode

                // Wrap in synthetic root node if a label is provided
                match rootLabel with
                | Some label -> [ DirectoryNode(label, "__root__", internalRoots) ]
                | None -> internalRoots

        /// Sort nodes: directories first, then files, both alphabetically
        let rec sortNodes (nodes: SyncTreeNode list) : SyncTreeNode list =
            let dirs, files =
                nodes
                |> List.partition (function DirectoryNode _ -> true | FileNode _ -> false)

            let sortedDirs =
                dirs
                |> List.sortBy (function DirectoryNode(name, _, _) -> name | _ -> "")
                |> List.map (function
                    | DirectoryNode(name, path, children) -> DirectoryNode(name, path, sortNodes children)
                    | node -> node)

            let sortedFiles =
                files
                |> List.sortBy (function FileNode entry -> entry.FileName | _ -> "")

            sortedDirs @ sortedFiles

        /// Compress single-child directory chains into combined "parent/child" nodes
        let rec compressTree (nodes: SyncTreeNode list) : SyncTreeNode list =
            nodes |> List.map (function
                | DirectoryNode(name, path, children) ->
                    let compressed = compressTree children
                    match compressed with
                    | [ DirectoryNode(childName, childPath, grandChildren) ] ->
                        // Single directory child with no files at this level -> merge
                        let mergedName = name + "/" + childName
                        // Recurse to continue collapsing further single-child chains
                        let furtherCompressed = compressTree [ DirectoryNode(mergedName, childPath, grandChildren) ]
                        furtherCompressed.Head
                    | _ ->
                        DirectoryNode(name, path, compressed)
                | node -> node)

    /// Get operation icon and color
    let private getOperationStyle (op: FileOperation) : (string * IBrush) =
        match op with
        | NewFile -> ("+", Theme.Brushes.accentGreen :> IBrush)
        | Newer -> ("^", SolidColorBrush(Theme.accentYellow) :> IBrush)
        | Older -> ("v", SolidColorBrush(Theme.accentYellow) :> IBrush)
        | ExtraFile -> ("x", Theme.Brushes.accentRed :> IBrush)

    /// Create sync tree panel with expandable directories
    let private createSyncTreePanel (state: State) (dispatch: Msg -> unit) =
        if state.SyncOperations.IsEmpty && not state.IsLogExpanded then
            Border.create [ Border.isVisible false ] :> IView
        else
            // Count operations by type
            let newCount = state.SyncOperations |> List.filter (fun e -> e.Operation = NewFile) |> List.length
            let modifiedCount = state.SyncOperations |> List.filter (fun e -> e.Operation = Newer || e.Operation = Older) |> List.length
            let extraCount = state.SyncOperations |> List.filter (fun e -> e.Operation = ExtraFile) |> List.length

            // Build tree
            let tree =
                SyncTree.buildTree state.SyncTreeRootLabel state.SyncOperations
                |> SyncTree.sortNodes
                |> SyncTree.compressTree

            /// Render a file node
            let renderFileNode (entry: FileEntry) (indent: int) : IView =
                let (icon, color) = getOperationStyle entry.Operation
                let sizeText =
                    match entry.FileSize with
                    | Some size -> sprintf " (%s)" (formatBytes size)
                    | None -> ""

                let sizeView : IView list =
                    match entry.FileSize with
                    | Some _ ->
                        [ TextBlock.create [
                            TextBlock.text sizeText
                            TextBlock.foreground Theme.Brushes.textMuted
                            TextBlock.fontSize Theme.Typography.fontSizeXs
                          ] :> IView ]
                    | None -> []

                Border.create [
                    Border.margin (Thickness(float (indent * 20), 2.0, 0.0, 2.0))
                    Border.child (
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children (
                                [
                                    // Operation icon
                                    TextBlock.create [
                                        TextBlock.text icon
                                        TextBlock.foreground color
                                        TextBlock.fontSize Theme.Typography.fontSizeXs
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.width 16.0
                                        TextBlock.textAlignment TextAlignment.Center
                                    ] :> IView
                                    // File name
                                    TextBlock.create [
                                        TextBlock.text entry.FileName
                                        TextBlock.foreground color
                                        TextBlock.fontSize Theme.Typography.fontSizeXs
                                        TextBlock.fontFamily (FontFamily("Consolas, monospace"))
                                    ] :> IView
                                ] @ sizeView
                            )
                        ]
                    )
                ] :> IView

            /// Render a directory node (recursive)
            let rec renderDirNode (name: string) (path: string) (children: SyncTreeNode list) (indent: int) : IView =
                let isExpanded = state.ExpandedDirs.Contains(path)
                let childCount = children.Length
                let expandIcon = if isExpanded then "v" else ">"

                let childViews : IView list =
                    if isExpanded then
                        children |> List.map (fun child ->
                            match child with
                            | DirectoryNode(n, p, c) -> renderDirNode n p c (indent + 1)
                            | FileNode entry -> renderFileNode entry (indent + 1))
                    else
                        []

                Border.create [
                    Border.margin (Thickness(float (indent * 20), 0.0, 0.0, 0.0))
                    Border.child (
                        StackPanel.create [
                            StackPanel.children (
                                [
                                    // Directory header (clickable)
                                    Button.create [
                                        Button.horizontalAlignment HorizontalAlignment.Stretch
                                        Button.horizontalContentAlignment HorizontalAlignment.Left
                                        Button.background Theme.Brushes.transparent
                                        Button.padding (Thickness(4.0, 4.0, 4.0, 4.0))
                                        Button.onClick (fun _ -> dispatch (ToggleDirectory path))
                                        Button.content (
                                            StackPanel.create [
                                                StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 8.0
                                                StackPanel.children [
                                                    // Expand icon
                                                    TextBlock.create [
                                                        TextBlock.text expandIcon
                                                        TextBlock.foreground Theme.Brushes.textMuted
                                                        TextBlock.fontSize Theme.Typography.fontSizeXs
                                                        TextBlock.fontWeight FontWeight.Bold
                                                        TextBlock.width 12.0
                                                    ]
                                                    // Folder icon
                                                    TextBlock.create [
                                                        TextBlock.text "D"
                                                        TextBlock.foreground Theme.Brushes.secondary
                                                        TextBlock.fontSize Theme.Typography.fontSizeXs
                                                        TextBlock.fontWeight FontWeight.Bold
                                                    ]
                                                    // Directory name
                                                    TextBlock.create [
                                                        TextBlock.text name
                                                        TextBlock.foreground Theme.Brushes.textPrimary
                                                        TextBlock.fontSize Theme.Typography.fontSizeXs
                                                        TextBlock.fontWeight FontWeight.Medium
                                                        TextBlock.fontFamily (FontFamily("Consolas, monospace"))
                                                    ]
                                                    // Child count
                                                    TextBlock.create [
                                                        TextBlock.text (sprintf "(%d)" childCount)
                                                        TextBlock.foreground Theme.Brushes.textMuted
                                                        TextBlock.fontSize Theme.Typography.fontSizeXs
                                                    ]
                                                ]
                                            ]
                                        )
                                    ] :> IView
                                ] @ childViews
                            )
                        ]
                    )
                ] :> IView

            /// Render tree nodes at a given level
            let renderNodes (nodes: SyncTreeNode list) (indent: int) : IView list =
                nodes |> List.map (fun node ->
                    match node with
                    | DirectoryNode(name, path, children) -> renderDirNode name path children indent
                    | FileNode entry -> renderFileNode entry indent
                )

            // Build buttons for expand/collapse
            let expandCollapseButtons : IView list =
                if state.IsLogExpanded && state.SyncOperations.Length > 0 then
                    [
                        Button.create [
                            Button.content "Expand All"
                            Button.background Theme.Brushes.transparent
                            Button.foreground Theme.Brushes.textMuted
                            Button.fontSize Theme.Typography.fontSizeXs
                            Button.padding (Thickness(8.0, 2.0, 8.0, 2.0))
                            Button.onClick (fun e ->
                                e.Handled <- true
                                dispatch ExpandAllDirs)
                        ] :> IView
                        Button.create [
                            Button.content "Collapse All"
                            Button.background Theme.Brushes.transparent
                            Button.foreground Theme.Brushes.textMuted
                            Button.fontSize Theme.Typography.fontSizeXs
                            Button.padding (Thickness(8.0, 2.0, 8.0, 2.0))
                            Button.onClick (fun e ->
                                e.Handled <- true
                                dispatch CollapseAllDirs)
                        ] :> IView
                    ]
                else
                    []

            // Build summary badges
            let summaryBadges : IView list =
                if state.SyncOperations.Length > 0 then
                    let badges =
                        [
                            if newCount > 0 then
                                yield TextBlock.create [
                                    TextBlock.text (sprintf "%d new" newCount)
                                    TextBlock.foreground Theme.Brushes.accentGreen
                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                ] :> IView
                            if modifiedCount > 0 then
                                yield TextBlock.create [
                                    TextBlock.text (sprintf "%d modified" modifiedCount)
                                    TextBlock.foreground (SolidColorBrush(Theme.accentYellow))
                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                ] :> IView
                            if extraCount > 0 then
                                yield TextBlock.create [
                                    TextBlock.text (sprintf "%d extra" extraCount)
                                    TextBlock.foreground Theme.Brushes.accentRed
                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                ] :> IView
                        ]
                    [
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 12.0
                            StackPanel.children badges
                        ] :> IView
                    ]
                else
                    []

            // Build tree content (no inner ScrollViewer — the outer page ScrollViewer handles scrolling)
            let treeContent : IView list =
                if state.IsLogExpanded then
                    [
                        StackPanel.create [
                            StackPanel.margin (Thickness(16.0))
                            StackPanel.children (renderNodes tree 0)
                        ] :> IView
                    ]
                else
                    []

            Border.create [
                Border.cornerRadius 12.0
                Border.background Theme.Brushes.surface
                Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
                Border.child (
                    StackPanel.create [
                        StackPanel.children (
                            [
                                // Header
                                Button.create [
                                    Button.horizontalAlignment HorizontalAlignment.Stretch
                                    Button.horizontalContentAlignment HorizontalAlignment.Left
                                    Button.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                                    Button.padding 16.0
                                    Button.cornerRadius 0.0
                                    Button.onClick (fun _ -> dispatch ToggleLogPanel)
                                    Button.content (
                                        DockPanel.create [
                                            DockPanel.children [
                                                // Expand/Collapse buttons (right)
                                                StackPanel.create [
                                                    DockPanel.dock Dock.Right
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 8.0
                                                    StackPanel.children (
                                                        expandCollapseButtons @
                                                        [
                                                            TextBlock.create [
                                                                TextBlock.text (if state.IsLogExpanded then "-" else "+")
                                                                TextBlock.foreground Theme.Brushes.textMuted
                                                                TextBlock.fontSize Theme.Typography.fontSizeMd
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                                            ] :> IView
                                                        ]
                                                    )
                                                ]
                                                // Title and summary
                                                StackPanel.create [
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 16.0
                                                    StackPanel.children (
                                                        [
                                                            TextBlock.create [
                                                                TextBlock.text (sprintf "Sync Operations (%d files)" state.SyncOperations.Length)
                                                                TextBlock.foreground Theme.Brushes.textPrimary
                                                                TextBlock.fontSize Theme.Typography.fontSizeSm
                                                                TextBlock.fontWeight FontWeight.Medium
                                                            ] :> IView
                                                        ] @ summaryBadges
                                                    )
                                                ]
                                            ]
                                        ]
                                    )
                                ] :> IView
                            ] @ treeContent
                        )
                    ]
                )
            ] :> IView

    let view (state: State) (dispatch: Msg -> unit) =
        Grid.create [
            Grid.children [
                ScrollViewer.create [
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.margin (Thickness(0.0, 0.0, 0.0, 32.0))
                            StackPanel.children [
                                // Header with title and Run All button
                                DockPanel.create [
                                    DockPanel.margin (Thickness(0.0, 0.0, 0.0, 24.0))
                                    DockPanel.children [
                                        // Run All Backups button (right)
                                        Button.create [
                                            DockPanel.dock Dock.Right
                                            Button.content (
                                                StackPanel.create [
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 8.0
                                                    StackPanel.children [
                                                        TextBlock.create [
                                                            TextBlock.text "Run All Backups"
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                                        ]
                                                    ]
                                                ]
                                            )
                                            Button.padding (Thickness(20.0, 12.0, 20.0, 12.0))
                                            Button.background Theme.Brushes.primary
                                            Button.foreground (SolidColorBrush(Colors.White))
                                            Button.cornerRadius 8.0
                                            Button.isEnabled false
                                        ]

                                        // Title (left)
                                        StackPanel.create [
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text "System Overview"
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeXxl
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "Manage and monitor your backup sources."
                                                    TextBlock.foreground Theme.Brushes.textMuted
                                                    TextBlock.fontSize Theme.Typography.fontSizeMd
                                                    TextBlock.margin (Thickness(0.0, 4.0, 0.0, 0.0))
                                                ]
                                            ]
                                        ]
                                    ]
                                ]

                                // VeraCrypt status card
                                createVeraCryptCard state.VeraCryptStatus dispatch

                                // Sync control panel
                                createSyncControlPanel state dispatch

                                // Log panel
                                createSyncTreePanel state dispatch

                                // Download state banner
                                match state.DownloadState with
                                | Idle -> ()
                                | Downloading progressInfo ->
                                    createDownloadProgress progressInfo
                                | Completed results ->
                                    createResultsBanner results dispatch
                                | Error message ->
                                    createErrorBanner message dispatch

                                // System metrics row
                                Grid.create [
                                    Grid.columnDefinitions "*, 16, *, 16, *, 16, *"
                                    Grid.margin (Thickness(0.0, 0.0, 0.0, 32.0))
                                    Grid.children [
                                        Border.create [
                                            Grid.column 0
                                            Border.child (
                                                createMetricCard "S" Theme.Brushes.primary "SOURCES" (sprintf "%d Active" state.Sources.Length) None
                                            )
                                        ]
                                        Border.create [
                                            Grid.column 2
                                            Border.child (
                                                createMetricCard "R" Theme.Brushes.accentPink "LAST REFRESH" (state.LastRefresh.ToString("HH:mm")) None
                                            )
                                        ]
                                        Border.create [
                                            Grid.column 4
                                            Border.child (
                                                let (linuxText, linuxColor) =
                                                    match state.LinuxServerStatus with
                                                    | NotConfigured -> ("Not Configured", Theme.Brushes.textMuted :> IBrush)
                                                    | Configured count -> (sprintf "%d Folders" count, Theme.Brushes.secondary :> IBrush)
                                                createMetricCard "L" Theme.Brushes.secondary "LINUX SERVER" linuxText (
                                                    match state.LinuxServerStatus with
                                                    | NotConfigured -> None
                                                    | Configured _ -> Some (createHealthBadge "Online" Theme.Brushes.accentGreen)
                                                )
                                            )
                                        ]
                                        Border.create [
                                            Grid.column 6
                                            Border.child (
                                                let (healthText, healthColor) = getSystemHealth state.Sources
                                                createMetricCard "H" Theme.Brushes.accentGreen "SYSTEM STATUS" healthText (Some (createHealthBadge healthText healthColor))
                                            )
                                        ]
                                    ]
                                ]

                                // Active Sources section
                                StackPanel.create [
                                    StackPanel.margin (Thickness(0.0, 0.0, 0.0, 32.0))
                                    StackPanel.children [
                                        DockPanel.create [
                                            DockPanel.margin (Thickness(0.0, 0.0, 0.0, 16.0))
                                            DockPanel.children [
                                                Button.create [
                                                    DockPanel.dock Dock.Right
                                                    Button.content "Refresh"
                                                    Button.background Theme.Brushes.transparent
                                                    Button.foreground Theme.Brushes.primary
                                                    Button.fontSize Theme.Typography.fontSizeSm
                                                    Button.fontWeight FontWeight.Medium
                                                    Button.padding (Thickness(8.0, 4.0, 8.0, 4.0))
                                                    Button.onClick (fun _ -> dispatch Refresh)
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "Active Sources"
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeLg
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                            ]
                                        ]

                                        // Source cards grid
                                        WrapPanel.create [
                                            WrapPanel.orientation Orientation.Horizontal
                                            WrapPanel.children [
                                                for source in state.Sources do
                                                    let onRunNow =
                                                        match source.SourceType, state.DownloadState with
                                                        | LinuxServer _, Idle ->
                                                            Some (fun () -> dispatch (StartLinuxDownload source.Name))
                                                        | LinuxServer _, _ ->
                                                            None // Disable during download
                                                        | _, _ ->
                                                            None // Not supported for other source types
                                                    Border.create [
                                                        Border.width 320.0
                                                        Border.margin (Thickness(0.0, 0.0, 16.0, 16.0))
                                                        Border.child (SourceCard.view source onRunNow)
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]

                                // Recent Activity section
                                createRecentActivityPlaceholder ()
                            ]
                        ]
                    )
                ]

                // Password dialog overlay
                Components.PasswordDialog.view
                    state.PasswordDialog
                    (fun pwd -> dispatch (SetPassword pwd))
                    (fun () -> dispatch MountVolume)
                    (fun () -> dispatch ClosePasswordDialog)
            ]
        ]
