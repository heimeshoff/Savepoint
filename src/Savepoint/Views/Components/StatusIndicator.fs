namespace Savepoint.Views.Components

open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Savepoint
open Savepoint.Domain

/// Status indicator dot component showing staleness level
module StatusIndicator =

    /// Get the color for a staleness level
    let getColor (staleness: StalenessLevel) : IBrush =
        match staleness with
        | Fresh -> Theme.Brushes.accentGreen :> IBrush
        | Stale -> SolidColorBrush(Theme.accentYellow) :> IBrush
        | Critical -> Theme.Brushes.accentRed :> IBrush
        | Unknown -> SolidColorBrush(Color.Parse("#64748b")) :> IBrush  // Slate-500

    /// Get the tooltip text for a staleness level
    let getTooltip (staleness: StalenessLevel) : string =
        match staleness with
        | Fresh -> "Healthy - Backup is up to date"
        | Stale -> "Attention - Backup is getting old"
        | Critical -> "Critical - Backup urgently needed"
        | Unknown -> "Unknown - No backup found"

    /// Create a status indicator dot
    let view (staleness: StalenessLevel) =
        Border.create [
            Border.width 10.0
            Border.height 10.0
            Border.cornerRadius 5.0
            Border.background (getColor staleness)
            ToolTip.tip (getTooltip staleness)
        ]

    /// Create a larger status indicator with label
    let viewWithLabel (staleness: StalenessLevel) =
        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 8.0
            StackPanel.verticalAlignment VerticalAlignment.Center
            StackPanel.children [
                view staleness
                TextBlock.create [
                    TextBlock.text (
                        match staleness with
                        | Fresh -> "Fresh"
                        | Stale -> "Stale"
                        | Critical -> "Critical"
                        | Unknown -> "Unknown"
                    )
                    TextBlock.foreground (getColor staleness)
                    TextBlock.fontSize Theme.Typography.fontSizeSm
                    TextBlock.fontWeight FontWeight.Medium
                ]
            ]
        ]
