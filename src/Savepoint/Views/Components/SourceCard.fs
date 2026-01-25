namespace Savepoint.Views.Components

open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Savepoint
open Savepoint.Domain
open Savepoint.Services

/// Source card component displaying backup source status
module SourceCard =

    /// Get the icon and color for a source type
    let private getSourceStyle (sourceType: SourceType) : string * IBrush =
        match sourceType with
        | NotionExport ->
            "N", SolidColorBrush(Color.Parse("#ffffff")) :> IBrush
        | GoogleTakeout ->
            "G", Theme.Brushes.accentGreen :> IBrush
        | LinuxServer _ ->
            "L", Theme.Brushes.secondary :> IBrush

    /// Get the background color for icon based on source type
    let private getIconBackground (sourceType: SourceType) : IBrush =
        match sourceType with
        | NotionExport ->
            SolidColorBrush(Color.FromArgb(byte 25, byte 255, byte 255, byte 255)) :> IBrush
        | GoogleTakeout ->
            SolidColorBrush(Color.FromArgb(byte 25, Theme.accentGreen.R, Theme.accentGreen.G, Theme.accentGreen.B)) :> IBrush
        | LinuxServer _ ->
            SolidColorBrush(Color.FromArgb(byte 25, Theme.secondary.R, Theme.secondary.G, Theme.secondary.B)) :> IBrush

    /// Create the icon element
    let private createIcon (sourceType: SourceType) =
        let (iconText, iconColor) = getSourceStyle sourceType

        Border.create [
            Border.padding 10.0
            Border.cornerRadius 8.0
            Border.background (getIconBackground sourceType)
            Border.child (
                TextBlock.create [
                    TextBlock.text iconText
                    TextBlock.foreground iconColor
                    TextBlock.fontSize 18.0
                    TextBlock.fontWeight FontWeight.Bold
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        ]

    /// Create a source card with optional run callback
    let view (source: BackupSource) (onRunNow: (unit -> unit) option) =
        Border.create [
            Border.cornerRadius Theme.Sizes.cardRadius
            Border.background Theme.Brushes.surface
            Border.padding 20.0
            Border.borderBrush Theme.Brushes.transparent
            Border.borderThickness 0.0
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 16.0
                    StackPanel.children [
                        // Header: Icon, Name, Status
                        DockPanel.create [
                            DockPanel.children [
                                // Status indicator (right side)
                                Border.create [
                                    DockPanel.dock Dock.Right
                                    Border.verticalAlignment VerticalAlignment.Top
                                    Border.child (StatusIndicator.view source.Staleness)
                                ]
                                // Icon and name (left side)
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.children [
                                        createIcon source.SourceType
                                        StackPanel.create [
                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text source.Name
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeMd
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text source.Description
                                                    TextBlock.foreground Theme.Brushes.textMuted
                                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]

                        // Stats row: Last Backup | Next Run
                        Border.create [
                            Border.borderThickness 0.0
                            Border.borderBrush Theme.Brushes.transparent
                            Border.padding (Thickness(0.0, 12.0, 0.0, 12.0))
                            Border.child (
                                Grid.create [
                                    Grid.columnDefinitions "*, *"
                                    Grid.children [
                                        // Last Backup
                                        StackPanel.create [
                                            Grid.column 0
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text "LAST BACKUP"
                                                    TextBlock.foreground Theme.Brushes.textMuted
                                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                                    TextBlock.margin (Thickness(0.0, 0.0, 0.0, 4.0))
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text (Staleness.formatRelativeTime source.LastBackup)
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                                    TextBlock.fontWeight FontWeight.Medium
                                                ]
                                            ]
                                        ]
                                        // Next Run
                                        StackPanel.create [
                                            Grid.column 1
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text "NEXT RUN"
                                                    TextBlock.foreground Theme.Brushes.textMuted
                                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                                    TextBlock.margin (Thickness(0.0, 0.0, 0.0, 4.0))
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "Manual"
                                                    TextBlock.foreground Theme.Brushes.textPrimary
                                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                                    TextBlock.fontWeight FontWeight.Medium
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            )
                        ]

                        // Storage progress
                        StackPanel.create [
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                DockPanel.create [
                                    DockPanel.children [
                                        TextBlock.create [
                                            DockPanel.dock Dock.Left
                                            TextBlock.text "Storage Used"
                                            TextBlock.foreground Theme.Brushes.textSecondary
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                            TextBlock.fontWeight FontWeight.Medium
                                        ]
                                        TextBlock.create [
                                            DockPanel.dock Dock.Right
                                            TextBlock.text (Staleness.formatStorage source.StorageUsedMB source.StorageTotalMB)
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                            TextBlock.horizontalAlignment HorizontalAlignment.Right
                                        ]
                                    ]
                                ]
                                ProgressBar.viewSimple
                                    (Staleness.storagePercentage source.StorageUsedMB source.StorageTotalMB)
                                    280.0
                            ]
                        ]

                        // Action buttons
                        Grid.create [
                            Grid.columnDefinitions "*, 8, *"
                            Grid.margin (Thickness(0.0, 4.0, 0.0, 0.0))
                            Grid.children [
                                Button.create [
                                    Grid.column 0
                                    Button.content "Log"
                                    Button.horizontalAlignment HorizontalAlignment.Stretch
                                    Button.horizontalContentAlignment HorizontalAlignment.Center
                                    Button.padding (Thickness(0.0, 8.0, 0.0, 8.0))
                                    Button.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                                    Button.foreground Theme.Brushes.textPrimary
                                    Button.fontSize Theme.Typography.fontSizeSm
                                    Button.fontWeight FontWeight.Medium
                                    Button.cornerRadius 8.0
                                    Button.isEnabled false
                                ]
                                Button.create [
                                    Grid.column 2
                                    Button.content "Run Now"
                                    Button.horizontalAlignment HorizontalAlignment.Stretch
                                    Button.horizontalContentAlignment HorizontalAlignment.Center
                                    Button.padding (Thickness(0.0, 8.0, 0.0, 8.0))
                                    Button.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.primary.R, Theme.primary.G, Theme.primary.B)))
                                    Button.foreground Theme.Brushes.primary
                                    Button.fontSize Theme.Typography.fontSizeSm
                                    Button.fontWeight FontWeight.Bold
                                    Button.cornerRadius 8.0
                                    Button.isEnabled (Option.isSome onRunNow)
                                    Button.onClick (fun _ ->
                                        onRunNow |> Option.iter (fun f -> f ()))
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]
