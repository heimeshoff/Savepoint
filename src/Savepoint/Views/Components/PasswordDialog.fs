namespace Savepoint.Views.Components

open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Savepoint

/// Modal dialog for entering VeraCrypt password
module PasswordDialog =

    type State = {
        IsOpen: bool
        Password: string
        IsLoading: bool
        Error: string option
        VolumeName: string
    }

    let empty = {
        IsOpen = false
        Password = ""
        IsLoading = false
        Error = None
        VolumeName = ""
    }

    let open' (volumeName: string) = {
        IsOpen = true
        Password = ""
        IsLoading = false
        Error = None
        VolumeName = volumeName
    }

    let setLoading (state: State) = { state with IsLoading = true; Error = None }

    let setError (error: string) (state: State) = { state with IsLoading = false; Error = Some error }

    let close () = empty

    /// Create the password dialog overlay
    let view
        (state: State)
        (onPasswordChanged: string -> unit)
        (onMount: unit -> unit)
        (onCancel: unit -> unit)
        : IView =

        if not state.IsOpen then
            Border.create [ Border.isVisible false ] :> IView
        else
            // Full-screen overlay
            Border.create [
                Border.background (SolidColorBrush(Color.FromArgb(byte 180, byte 0, byte 0, byte 0)))
                Border.horizontalAlignment HorizontalAlignment.Stretch
                Border.verticalAlignment VerticalAlignment.Stretch
                Border.child (
                    Border.create [
                        Border.width 400.0
                        Border.cornerRadius 12.0
                        Border.background Theme.Brushes.surface
                        Border.horizontalAlignment HorizontalAlignment.Center
                        Border.verticalAlignment VerticalAlignment.Center
                        Border.padding 24.0
                        Border.child (
                            StackPanel.create [
                                StackPanel.spacing 20.0
                                StackPanel.children [
                                    // Header
                                    StackPanel.create [
                                        StackPanel.children [
                                            TextBlock.create [
                                                TextBlock.text "Mount VeraCrypt Volume"
                                                TextBlock.foreground Theme.Brushes.textPrimary
                                                TextBlock.fontSize Theme.Typography.fontSizeLg
                                                TextBlock.fontWeight FontWeight.Bold
                                            ]
                                            if not (System.String.IsNullOrEmpty(state.VolumeName)) then
                                                TextBlock.create [
                                                    TextBlock.text state.VolumeName
                                                    TextBlock.foreground Theme.Brushes.textMuted
                                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                                    TextBlock.margin (Thickness(0.0, 4.0, 0.0, 0.0))
                                                ]
                                        ]
                                    ]

                                    // Password field
                                    StackPanel.create [
                                        StackPanel.spacing 8.0
                                        StackPanel.children [
                                            TextBlock.create [
                                                TextBlock.text "Password"
                                                TextBlock.foreground Theme.Brushes.textSecondary
                                                TextBlock.fontSize Theme.Typography.fontSizeSm
                                                TextBlock.fontWeight FontWeight.Medium
                                            ]
                                            TextBox.create [
                                                TextBox.text state.Password
                                                TextBox.passwordChar '*'
                                                TextBox.watermark "Enter volume password"
                                                TextBox.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                                                TextBox.foreground Theme.Brushes.textPrimary
                                                TextBox.borderBrush Theme.Brushes.border
                                                TextBox.borderThickness 1.0
                                                TextBox.padding (Thickness(12.0, 10.0, 12.0, 10.0))
                                                TextBox.cornerRadius 8.0
                                                TextBox.fontSize Theme.Typography.fontSizeMd
                                                TextBox.isEnabled (not state.IsLoading)
                                                TextBox.onTextChanged onPasswordChanged
                                            ]
                                        ]
                                    ]

                                    // Error message
                                    match state.Error with
                                    | Some err ->
                                        Border.create [
                                            Border.background (SolidColorBrush(Color.FromArgb(byte 25, byte 231, byte 76, byte 60)))
                                            Border.cornerRadius 8.0
                                            Border.padding 12.0
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text err
                                                    TextBlock.foreground Theme.Brushes.accentRed
                                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                                    TextBlock.textWrapping TextWrapping.Wrap
                                                ]
                                            )
                                        ]
                                    | None -> ()

                                    // Buttons
                                    StackPanel.create [
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 12.0
                                        StackPanel.horizontalAlignment HorizontalAlignment.Right
                                        StackPanel.children [
                                            Button.create [
                                                Button.content "Cancel"
                                                Button.padding (Thickness(20.0, 10.0, 20.0, 10.0))
                                                Button.background Theme.Brushes.transparent
                                                Button.foreground Theme.Brushes.textMuted
                                                Button.fontSize Theme.Typography.fontSizeSm
                                                Button.cornerRadius 8.0
                                                Button.isEnabled (not state.IsLoading)
                                                Button.onClick (fun _ -> onCancel ())
                                            ]
                                            Button.create [
                                                Button.content (
                                                    if state.IsLoading then "Mounting..."
                                                    else "Mount"
                                                )
                                                Button.padding (Thickness(20.0, 10.0, 20.0, 10.0))
                                                Button.background Theme.Brushes.primary
                                                Button.foreground (SolidColorBrush(Colors.White))
                                                Button.fontSize Theme.Typography.fontSizeSm
                                                Button.fontWeight FontWeight.Bold
                                                Button.cornerRadius 8.0
                                                Button.isEnabled (
                                                    not state.IsLoading &&
                                                    not (System.String.IsNullOrWhiteSpace(state.Password))
                                                )
                                                Button.onClick (fun _ -> onMount ())
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        )
                    ]
                )
            ] :> IView
