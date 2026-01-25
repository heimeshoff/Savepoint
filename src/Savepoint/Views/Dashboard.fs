namespace Savepoint.Views

open System
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
    }

    type Msg =
        | Refresh
        | RefreshComplete of BackupSource list
        | StartLinuxDownload of folderName: string
        | DownloadProgressUpdate of DownloadProgressInfo
        | DownloadComplete of DownloadResult list
        | DownloadError of string
        | DismissResults

    let private getLinuxServerStatus (config: AppConfig) =
        match config.LinuxServerHost with
        | Some host when not (String.IsNullOrWhiteSpace(host)) ->
            Configured config.LinuxServerFolders.Length
        | _ -> NotConfigured

    let init (config: AppConfig) =
        { Sources = Staleness.scanAll config
          LastRefresh = DateTime.Now
          LinuxServerStatus = getLinuxServerStatus config
          DownloadState = Idle }

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

    let update (config: AppConfig) (msg: Msg) (state: State) : State * Elmish.Cmd<Msg> =
        match msg with
        | Refresh ->
            { state with
                Sources = Staleness.scanAll config
                LastRefresh = DateTime.Now
                LinuxServerStatus = getLinuxServerStatus config }, Elmish.Cmd.none
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

    let view (state: State) (dispatch: Msg -> unit) =
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
