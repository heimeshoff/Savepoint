namespace Savepoint.Views

open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Savepoint
open Savepoint.Domain
open Savepoint.Services

/// Settings view for configuration management
module Settings =

    type State = {
        Config: AppConfig
        SaveStatus: string option
        IsDirty: bool
    }

    type Msg =
        | SetGoogleDrivePath of string
        | SetNotionPath of string
        | SetGoogleTakeoutPath of string
        | SetLinuxServerHost of string
        | Save
        | Reload

    let init () =
        { Config = Config.load ()
          SaveStatus = None
          IsDirty = false }

    let update (msg: Msg) (state: State) : State =
        match msg with
        | SetGoogleDrivePath path ->
            { state with
                Config = { state.Config with GoogleDrivePath = path }
                IsDirty = true
                SaveStatus = None }
        | SetNotionPath path ->
            { state with
                Config = { state.Config with NotionPath = path }
                IsDirty = true
                SaveStatus = None }
        | SetGoogleTakeoutPath path ->
            { state with
                Config = { state.Config with GoogleTakeoutPath = path }
                IsDirty = true
                SaveStatus = None }
        | SetLinuxServerHost host ->
            { state with
                Config = { state.Config with LinuxServerHost = if System.String.IsNullOrWhiteSpace(host) then None else Some host }
                IsDirty = true
                SaveStatus = None }
        | Save ->
            match Config.save state.Config with
            | Result.Ok () ->
                { state with SaveStatus = Some "Settings saved successfully"; IsDirty = false }
            | Result.Error msg ->
                { state with SaveStatus = Some (sprintf "Error: %s" msg) }
        | Reload ->
            { state with Config = Config.load (); IsDirty = false; SaveStatus = None }

    let private createTextField (label: string) (value: string) (placeholder: string) (onChange: string -> unit) =
        StackPanel.create [
            StackPanel.spacing 8.0
            StackPanel.margin (Thickness(0.0, 0.0, 0.0, 16.0))
            StackPanel.children [
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.foreground Theme.Brushes.textSecondary
                    TextBlock.fontSize Theme.Typography.fontSizeSm
                    TextBlock.fontWeight FontWeight.Medium
                ]
                TextBox.create [
                    TextBox.text value
                    TextBox.watermark placeholder
                    TextBox.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                    TextBox.foreground Theme.Brushes.textPrimary
                    TextBox.borderBrush Theme.Brushes.border
                    TextBox.borderThickness 1.0
                    TextBox.padding (Thickness(12.0, 10.0, 12.0, 10.0))
                    TextBox.cornerRadius 8.0
                    TextBox.fontSize Theme.Typography.fontSizeMd
                    TextBox.onTextChanged onChange
                ]
            ]
        ]

    let view (state: State) (dispatch: Msg -> unit) =
        ScrollViewer.create [
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.margin (Thickness(0.0, 0.0, 0.0, 32.0))
                    StackPanel.children [
                        // Header
                        StackPanel.create [
                            StackPanel.margin (Thickness(0.0, 0.0, 0.0, 24.0))
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text "Settings"
                                    TextBlock.foreground Theme.Brushes.textPrimary
                                    TextBlock.fontSize Theme.Typography.fontSizeXxl
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                TextBlock.create [
                                    TextBlock.text "Configure backup source paths and preferences."
                                    TextBlock.foreground Theme.Brushes.textMuted
                                    TextBlock.fontSize Theme.Typography.fontSizeMd
                                    TextBlock.margin (Thickness(0.0, 4.0, 0.0, 0.0))
                                ]
                            ]
                        ]

                        // Path Configuration Section
                        Border.create [
                            Border.cornerRadius Theme.Sizes.cardRadius
                            Border.background Theme.Brushes.surface
                            Border.padding 24.0
                            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
                            Border.child (
                                StackPanel.create [
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "Path Configuration"
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeLg
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (Thickness(0.0, 0.0, 0.0, 20.0))
                                        ]

                                        createTextField
                                            "Google Drive Path"
                                            state.Config.GoogleDrivePath
                                            @"e.g., G:\"
                                            (fun v -> dispatch (SetGoogleDrivePath v))

                                        createTextField
                                            "Notion Export Folder"
                                            state.Config.NotionPath
                                            @"e.g., G:\My Drive\notion"
                                            (fun v -> dispatch (SetNotionPath v))

                                        createTextField
                                            "Google Takeout Folder"
                                            state.Config.GoogleTakeoutPath
                                            @"e.g., G:\My Drive\google-takeout"
                                            (fun v -> dispatch (SetGoogleTakeoutPath v))
                                    ]
                                ]
                            )
                        ]

                        // Linux Server Section (placeholder for Phase 3)
                        Border.create [
                            Border.cornerRadius Theme.Sizes.cardRadius
                            Border.background Theme.Brushes.surface
                            Border.padding 24.0
                            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
                            Border.child (
                                StackPanel.create [
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "Linux Server (Coming in Phase 3)"
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeLg
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (Thickness(0.0, 0.0, 0.0, 20.0))
                                        ]

                                        createTextField
                                            "Server Host"
                                            (state.Config.LinuxServerHost |> Option.defaultValue "")
                                            "e.g., 192.168.1.100 or server.local"
                                            (fun v -> dispatch (SetLinuxServerHost v))

                                        TextBlock.create [
                                            TextBlock.text "SSH/SCP configuration will be available in a future update."
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                            TextBlock.fontStyle FontStyle.Italic
                                        ]
                                    ]
                                ]
                            )
                        ]

                        // Save Button and Status
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 16.0
                            StackPanel.horizontalAlignment HorizontalAlignment.Left
                            StackPanel.children [
                                Button.create [
                                    Button.content (if state.IsDirty then "Save Changes" else "Saved")
                                    Button.padding (Thickness(24.0, 12.0, 24.0, 12.0))
                                    Button.background (if state.IsDirty then Theme.Brushes.primary else Theme.Brushes.surfaceLight)
                                    Button.foreground (if state.IsDirty then SolidColorBrush(Colors.White) else Theme.Brushes.textMuted)
                                    Button.fontSize Theme.Typography.fontSizeSm
                                    Button.fontWeight FontWeight.Bold
                                    Button.cornerRadius 8.0
                                    Button.isEnabled state.IsDirty
                                    Button.onClick (fun _ -> dispatch Save)
                                ]

                                match state.SaveStatus with
                                | Some status ->
                                    TextBlock.create [
                                        TextBlock.text status
                                        TextBlock.foreground (
                                            if status.StartsWith("Error") then Theme.Brushes.accentRed
                                            else Theme.Brushes.accentGreen
                                        )
                                        TextBlock.fontSize Theme.Typography.fontSizeSm
                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                    ]
                                | None -> ()
                            ]
                        ]
                    ]
                ]
            )
        ]
