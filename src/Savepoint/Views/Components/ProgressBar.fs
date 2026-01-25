namespace Savepoint.Views.Components

open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Savepoint

/// Progress bar component with gradient fill
module ProgressBar =

    /// Create a gradient brush for the progress fill
    let private createGradient () : IBrush =
        let gradient = LinearGradientBrush()
        gradient.StartPoint <- RelativePoint(0.0, 0.5, RelativeUnit.Relative)
        gradient.EndPoint <- RelativePoint(1.0, 0.5, RelativeUnit.Relative)
        gradient.GradientStops.Add(GradientStop(Theme.primary, 0.0))
        gradient.GradientStops.Add(GradientStop(Color.FromArgb(byte 153, Theme.primary.R, Theme.primary.G, Theme.primary.B), 1.0))
        gradient :> IBrush

    /// Simpler progress bar that works well with FuncUI
    let viewSimple (percentage: float) (width: float) =
        let clampedPercentage = percentage |> max 0.0 |> min 100.0
        let fillWidth = width * clampedPercentage / 100.0

        Grid.create [
            Grid.width width
            Grid.height 8.0
            Grid.children [
                // Background track
                Border.create [
                    Border.cornerRadius 4.0
                    Border.background (SolidColorBrush(Color.Parse("#374151")))
                ]
                // Fill
                Border.create [
                    Border.horizontalAlignment HorizontalAlignment.Left
                    Border.width fillWidth
                    Border.cornerRadius 4.0
                    Border.background (createGradient ())
                ]
            ]
        ]
