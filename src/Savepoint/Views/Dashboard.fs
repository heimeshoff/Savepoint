namespace Savepoint.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Savepoint
open Savepoint.Domain
open Savepoint.Services
open Savepoint.Views.Components

/// Dashboard view showing backup source overview
module Dashboard =

    type State = {
        Sources: BackupSource list
        LastRefresh: DateTime
    }

    type Msg =
        | Refresh
        | RefreshComplete of BackupSource list

    let init (config: AppConfig) =
        { Sources = Staleness.scanAll config
          LastRefresh = DateTime.Now }

    let update (config: AppConfig) (msg: Msg) (state: State) : State =
        match msg with
        | Refresh ->
            { state with
                Sources = Staleness.scanAll config
                LastRefresh = DateTime.Now }
        | RefreshComplete sources ->
            { state with Sources = sources; LastRefresh = DateTime.Now }

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

                        // System metrics row
                        Grid.create [
                            Grid.columnDefinitions "*, 16, *, 16, *"
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
                                        let (healthText, healthColor) = getSystemHealth state.Sources
                                        createMetricCard "H" Theme.Brushes.secondary "SYSTEM STATUS" healthText (Some (createHealthBadge healthText healthColor))
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
                                            Border.create [
                                                Border.width 320.0
                                                Border.margin (Thickness(0.0, 0.0, 16.0, 16.0))
                                                Border.child (SourceCard.view source)
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
