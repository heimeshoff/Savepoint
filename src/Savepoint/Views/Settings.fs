namespace Savepoint.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Savepoint
open Savepoint.Domain
open Savepoint.Services

/// Settings view for configuration management
module Settings =

    type ConnectionTestStatus =
        | NotTested
        | Testing
        | TestSuccess
        | TestFailed of string

    /// State for editing a new folder
    type NewFolderState = {
        LocalName: string
        RemotePath: string
        LocalPath: string
        FilePattern: string
        IsExpanded: bool
    }

    type State = {
        Config: AppConfig
        SaveStatus: string option
        IsDirty: bool
        ConnectionTestStatus: ConnectionTestStatus
        NewFolder: NewFolderState
    }

    type Msg =
        | SetGoogleDrivePath of string
        | SetNotionPath of string
        | SetGoogleTakeoutPath of string
        | SetLinuxServerHost of string
        | SetLinuxServerPort of string
        | SetLinuxServerUser of string
        | SetLinuxServerKeyPath of string
        | SetPassphrase of string
        | TestConnection
        | TestConnectionResult of SshConnection.ConnectionStatus
        | ToggleNewFolderForm
        | SetNewFolderLocalName of string
        | SetNewFolderRemotePath of string
        | SetNewFolderLocalPath of string
        | SetNewFolderFilePattern of string
        | AddFolder
        | RemoveFolder of int
        | Save
        | Reload

    let private emptyNewFolder = {
        LocalName = ""
        RemotePath = ""
        LocalPath = ""
        FilePattern = "*.tar.gz"
        IsExpanded = false
    }

    let init () =
        { Config = Config.load ()
          SaveStatus = None
          IsDirty = false
          ConnectionTestStatus = NotTested
          NewFolder = emptyNewFolder }

    let update (msg: Msg) (state: State) : State * Elmish.Cmd<Msg> =
        match msg with
        | SetGoogleDrivePath path ->
            { state with
                Config = { state.Config with GoogleDrivePath = path }
                IsDirty = true
                SaveStatus = None }, Elmish.Cmd.none
        | SetNotionPath path ->
            { state with
                Config = { state.Config with NotionPath = path }
                IsDirty = true
                SaveStatus = None }, Elmish.Cmd.none
        | SetGoogleTakeoutPath path ->
            { state with
                Config = { state.Config with GoogleTakeoutPath = path }
                IsDirty = true
                SaveStatus = None }, Elmish.Cmd.none
        | SetLinuxServerHost host ->
            { state with
                Config = { state.Config with LinuxServerHost = if String.IsNullOrWhiteSpace(host) then None else Some host }
                IsDirty = true
                SaveStatus = None
                ConnectionTestStatus = NotTested }, Elmish.Cmd.none
        | SetLinuxServerPort portStr ->
            let port = match Int32.TryParse(portStr) with | true, p -> p | _ -> 22
            { state with
                Config = { state.Config with LinuxServerPort = port }
                IsDirty = true
                SaveStatus = None
                ConnectionTestStatus = NotTested }, Elmish.Cmd.none
        | SetLinuxServerUser user ->
            { state with
                Config = { state.Config with LinuxServerUser = if String.IsNullOrWhiteSpace(user) then None else Some user }
                IsDirty = true
                SaveStatus = None
                ConnectionTestStatus = NotTested }, Elmish.Cmd.none
        | SetLinuxServerKeyPath path ->
            { state with
                Config = { state.Config with LinuxServerKeyPath = if String.IsNullOrWhiteSpace(path) then None else Some path }
                IsDirty = true
                SaveStatus = None
                ConnectionTestStatus = NotTested }, Elmish.Cmd.none
        | SetPassphrase pass ->
            { state with
                Config = { state.Config with LinuxServerPassphrase = if String.IsNullOrWhiteSpace(pass) then None else Some pass }
                IsDirty = true
                SaveStatus = None
                ConnectionTestStatus = NotTested }, Elmish.Cmd.none
        | TestConnection ->
            let canTest =
                SshConnection.isConfigured
                    state.Config.LinuxServerHost
                    state.Config.LinuxServerUser
                    state.Config.LinuxServerKeyPath
            if canTest then
                let host = state.Config.LinuxServerHost.Value
                let port = state.Config.LinuxServerPort
                let user = state.Config.LinuxServerUser.Value
                let keyPath = state.Config.LinuxServerKeyPath.Value
                let creds = SshConnection.createCredentials host port user keyPath state.Config.LinuxServerPassphrase
                // Custom command that explicitly dispatches on UI thread
                let testCmd : Elmish.Cmd<Msg> =
                    [ fun dispatch ->
                        async {
                            let! result = SshConnection.testConnection creds
                            // Dispatch on UI thread
                            Dispatcher.UIThread.Post(fun () -> dispatch (TestConnectionResult result))
                        } |> Async.Start
                    ]
                { state with ConnectionTestStatus = Testing }, testCmd
            else
                { state with ConnectionTestStatus = TestFailed "Please fill in all connection fields" }, Elmish.Cmd.none
        | TestConnectionResult status ->
            // Log to the SSH log file so we can see if this message arrives
            let logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Savepoint", "ssh.log")
            let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            System.IO.File.AppendAllText(logPath, sprintf "[%s] TestConnectionResult received: %A\n" timestamp status)
            let newStatus =
                match status with
                | SshConnection.Connected -> TestSuccess
                | SshConnection.Disconnected -> TestFailed "Server disconnected"
                | SshConnection.Error msg -> TestFailed msg
            System.IO.File.AppendAllText(logPath, sprintf "[%s] Setting ConnectionTestStatus to: %A\n" timestamp newStatus)
            { state with ConnectionTestStatus = newStatus }, Elmish.Cmd.none
        | ToggleNewFolderForm ->
            { state with NewFolder = { state.NewFolder with IsExpanded = not state.NewFolder.IsExpanded } }, Elmish.Cmd.none
        | SetNewFolderLocalName name ->
            { state with NewFolder = { state.NewFolder with LocalName = name } }, Elmish.Cmd.none
        | SetNewFolderRemotePath path ->
            { state with NewFolder = { state.NewFolder with RemotePath = path } }, Elmish.Cmd.none
        | SetNewFolderLocalPath path ->
            { state with NewFolder = { state.NewFolder with LocalPath = path } }, Elmish.Cmd.none
        | SetNewFolderFilePattern pattern ->
            { state with NewFolder = { state.NewFolder with FilePattern = pattern } }, Elmish.Cmd.none
        | AddFolder ->
            let nf = state.NewFolder
            if String.IsNullOrWhiteSpace(nf.LocalName) ||
               String.IsNullOrWhiteSpace(nf.RemotePath) ||
               String.IsNullOrWhiteSpace(nf.LocalPath) then
                state, Elmish.Cmd.none
            else
                let newFolder: LinuxFolder = {
                    LocalName = nf.LocalName
                    RemotePath = nf.RemotePath
                    LocalPath = nf.LocalPath
                    FilePattern = if String.IsNullOrWhiteSpace(nf.FilePattern) then "*" else nf.FilePattern
                }
                { state with
                    Config = { state.Config with LinuxServerFolders = state.Config.LinuxServerFolders @ [newFolder] }
                    NewFolder = emptyNewFolder
                    IsDirty = true
                    SaveStatus = None }, Elmish.Cmd.none
        | RemoveFolder index ->
            let folders = state.Config.LinuxServerFolders |> List.indexed |> List.filter (fun (i, _) -> i <> index) |> List.map snd
            { state with
                Config = { state.Config with LinuxServerFolders = folders }
                IsDirty = true
                SaveStatus = None }, Elmish.Cmd.none
        | Save ->
            match Config.save state.Config with
            | Result.Ok () ->
                { state with SaveStatus = Some "Settings saved successfully"; IsDirty = false }, Elmish.Cmd.none
            | Result.Error msg ->
                { state with SaveStatus = Some (sprintf "Error: %s" msg) }, Elmish.Cmd.none
        | Reload ->
            { state with Config = Config.load (); IsDirty = false; SaveStatus = None; ConnectionTestStatus = NotTested; NewFolder = emptyNewFolder }, Elmish.Cmd.none

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

    let private createSmallTextField (label: string) (value: string) (placeholder: string) (width: float) (onChange: string -> unit) =
        StackPanel.create [
            StackPanel.spacing 4.0
            StackPanel.width width
            StackPanel.children [
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.foreground Theme.Brushes.textMuted
                    TextBlock.fontSize Theme.Typography.fontSizeXs
                ]
                TextBox.create [
                    TextBox.text value
                    TextBox.watermark placeholder
                    TextBox.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                    TextBox.foreground Theme.Brushes.textPrimary
                    TextBox.borderBrush Theme.Brushes.border
                    TextBox.borderThickness 1.0
                    TextBox.padding (Thickness(8.0, 6.0, 8.0, 6.0))
                    TextBox.cornerRadius 6.0
                    TextBox.fontSize Theme.Typography.fontSizeSm
                    TextBox.onTextChanged onChange
                ]
            ]
        ]

    let private createPasswordField (label: string) (value: string) (placeholder: string) (onChange: string -> unit) =
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
                    TextBox.passwordChar '*'
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

    let private createFolderRow (index: int) (folder: LinuxFolder) (dispatch: Msg -> unit) =
        Border.create [
            Border.cornerRadius 8.0
            Border.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
            Border.padding 12.0
            Border.margin (Thickness(0.0, 0.0, 0.0, 8.0))
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        // Remove button (right)
                        Button.create [
                            DockPanel.dock Dock.Right
                            Button.content "X"
                            Button.padding (Thickness(8.0, 4.0, 8.0, 4.0))
                            Button.background Theme.Brushes.transparent
                            Button.foreground Theme.Brushes.accentRed
                            Button.fontSize Theme.Typography.fontSizeSm
                            Button.fontWeight FontWeight.Bold
                            Button.cornerRadius 4.0
                            Button.verticalAlignment VerticalAlignment.Top
                            Button.onClick (fun _ -> dispatch (RemoveFolder index))
                        ]
                        // Folder info (left)
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text folder.LocalName
                                    TextBlock.foreground Theme.Brushes.textPrimary
                                    TextBlock.fontSize Theme.Typography.fontSizeMd
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text (sprintf "Remote: %s" folder.RemotePath)
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                        ]
                                        TextBlock.create [
                                            TextBlock.text "|"
                                            TextBlock.foreground Theme.Brushes.border
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                        ]
                                        TextBlock.create [
                                            TextBlock.text (sprintf "Pattern: %s" folder.FilePattern)
                                            TextBlock.foreground Theme.Brushes.textMuted
                                            TextBlock.fontSize Theme.Typography.fontSizeXs
                                        ]
                                    ]
                                ]
                                TextBlock.create [
                                    TextBlock.text (sprintf "Local: %s" folder.LocalPath)
                                    TextBlock.foreground Theme.Brushes.textMuted
                                    TextBlock.fontSize Theme.Typography.fontSizeXs
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    let private createNewFolderForm (state: State) (dispatch: Msg -> unit) =
        if not state.NewFolder.IsExpanded then
            Button.create [
                Button.content "+ Add Folder"
                Button.padding (Thickness(16.0, 10.0, 16.0, 10.0))
                Button.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.primary.R, Theme.primary.G, Theme.primary.B)))
                Button.foreground Theme.Brushes.primary
                Button.fontSize Theme.Typography.fontSizeSm
                Button.fontWeight FontWeight.Bold
                Button.cornerRadius 8.0
                Button.onClick (fun _ -> dispatch ToggleNewFolderForm)
            ] :> Avalonia.FuncUI.Types.IView
        else
            Border.create [
                Border.cornerRadius 8.0
                Border.background (SolidColorBrush(Color.FromArgb(byte 13, byte 255, byte 255, byte 255)))
                Border.padding 16.0
                Border.margin (Thickness(0.0, 8.0, 0.0, 0.0))
                Border.child (
                    StackPanel.create [
                        StackPanel.spacing 12.0
                        StackPanel.children [
                            TextBlock.create [
                                TextBlock.text "Add New Folder"
                                TextBlock.foreground Theme.Brushes.textPrimary
                                TextBlock.fontSize Theme.Typography.fontSizeMd
                                TextBlock.fontWeight FontWeight.Bold
                            ]
                            // Row 1: Name and Pattern
                            StackPanel.create [
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 12.0
                                StackPanel.children [
                                    createSmallTextField "Display Name" state.NewFolder.LocalName "e.g., Server Backups" 200.0 (fun v -> dispatch (SetNewFolderLocalName v))
                                    createSmallTextField "File Pattern" state.NewFolder.FilePattern "e.g., *.tar.gz" 150.0 (fun v -> dispatch (SetNewFolderFilePattern v))
                                ]
                            ]
                            // Row 2: Remote and Local paths
                            createSmallTextField "Remote Path" state.NewFolder.RemotePath "e.g., /home/user/backups" 400.0 (fun v -> dispatch (SetNewFolderRemotePath v))
                            createSmallTextField "Local Path (on GDrive)" state.NewFolder.LocalPath @"e.g., G:\My Drive\linux-backups" 400.0 (fun v -> dispatch (SetNewFolderLocalPath v))
                            // Buttons
                            StackPanel.create [
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 8.0
                                StackPanel.children [
                                    Button.create [
                                        Button.content "Add"
                                        Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                        Button.background Theme.Brushes.primary
                                        Button.foreground (SolidColorBrush(Colors.White))
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.fontWeight FontWeight.Bold
                                        Button.cornerRadius 6.0
                                        Button.isEnabled (
                                            not (String.IsNullOrWhiteSpace(state.NewFolder.LocalName)) &&
                                            not (String.IsNullOrWhiteSpace(state.NewFolder.RemotePath)) &&
                                            not (String.IsNullOrWhiteSpace(state.NewFolder.LocalPath))
                                        )
                                        Button.onClick (fun _ -> dispatch AddFolder)
                                    ]
                                    Button.create [
                                        Button.content "Cancel"
                                        Button.padding (Thickness(16.0, 8.0, 16.0, 8.0))
                                        Button.background Theme.Brushes.transparent
                                        Button.foreground Theme.Brushes.textMuted
                                        Button.fontSize Theme.Typography.fontSizeSm
                                        Button.cornerRadius 6.0
                                        Button.onClick (fun _ -> dispatch ToggleNewFolderForm)
                                    ]
                                ]
                            ]
                        ]
                    ]
                )
            ] :> Avalonia.FuncUI.Types.IView

    let view (state: State) (dispatch: Msg -> unit) =
        ScrollViewer.create [
            // Set dataContext to force Avalonia to recognize state change
            ScrollViewer.dataContext (box state.ConnectionTestStatus)
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

                        // Linux Server Section
                        Border.create [
                            Border.cornerRadius Theme.Sizes.cardRadius
                            Border.background Theme.Brushes.surface
                            Border.padding 24.0
                            Border.margin (Thickness(0.0, 0.0, 0.0, 16.0))
                            Border.child (
                                StackPanel.create [
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "Linux Server"
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeLg
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (Thickness(0.0, 0.0, 0.0, 20.0))
                                        ]

                                        // Connection settings row
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 12.0
                                            StackPanel.margin (Thickness(0.0, 0.0, 0.0, 16.0))
                                            StackPanel.children [
                                                createSmallTextField "Host" (state.Config.LinuxServerHost |> Option.defaultValue "") "192.168.1.100" 180.0 (fun v -> dispatch (SetLinuxServerHost v))
                                                createSmallTextField "Port" (string state.Config.LinuxServerPort) "22" 60.0 (fun v -> dispatch (SetLinuxServerPort v))
                                                createSmallTextField "Username" (state.Config.LinuxServerUser |> Option.defaultValue "") "user" 120.0 (fun v -> dispatch (SetLinuxServerUser v))
                                            ]
                                        ]

                                        createTextField
                                            "SSH Private Key Path"
                                            (state.Config.LinuxServerKeyPath |> Option.defaultValue "")
                                            @"e.g., C:\Users\you\.ssh\id_rsa"
                                            (fun v -> dispatch (SetLinuxServerKeyPath v))

                                        createPasswordField
                                            "SSH Key Passphrase (if required)"
                                            (state.Config.LinuxServerPassphrase |> Option.defaultValue "")
                                            "Enter passphrase for encrypted key"
                                            (fun v -> dispatch (SetPassphrase v))

                                        // Test Connection button and status
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 16.0
                                            StackPanel.margin (Thickness(0.0, 0.0, 0.0, 24.0))
                                            StackPanel.children [
                                                Button.create [
                                                    Button.content (
                                                        match state.ConnectionTestStatus with
                                                        | Testing -> "Testing..."
                                                        | _ -> "Test Connection"
                                                    )
                                                    Button.padding (Thickness(16.0, 10.0, 16.0, 10.0))
                                                    Button.background (SolidColorBrush(Color.FromArgb(byte 25, Theme.secondary.R, Theme.secondary.G, Theme.secondary.B)))
                                                    Button.foreground Theme.Brushes.secondary
                                                    Button.fontSize Theme.Typography.fontSizeSm
                                                    Button.fontWeight FontWeight.Bold
                                                    Button.cornerRadius 8.0
                                                    Button.isEnabled (state.ConnectionTestStatus <> Testing)
                                                    Button.onClick (fun _ -> dispatch TestConnection)
                                                ]
                                                // Connection status display
                                                StackPanel.create [
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 8.0
                                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                                    StackPanel.isVisible (state.ConnectionTestStatus <> NotTested)
                                                    StackPanel.children [
                                                        Border.create [
                                                            Border.width 8.0
                                                            Border.height 8.0
                                                            Border.cornerRadius 4.0
                                                            Border.background (
                                                                match state.ConnectionTestStatus with
                                                                | TestSuccess -> Theme.Brushes.accentGreen
                                                                | TestFailed _ -> Theme.Brushes.accentRed
                                                                | _ -> Theme.Brushes.textMuted
                                                            )
                                                            Border.isVisible (state.ConnectionTestStatus <> Testing)
                                                        ]
                                                        TextBlock.create [
                                                            TextBlock.text (
                                                                match state.ConnectionTestStatus with
                                                                | NotTested -> ""
                                                                | Testing -> "Connecting..."
                                                                | TestSuccess -> "Connected"
                                                                | TestFailed msg -> msg
                                                            )
                                                            TextBlock.foreground (
                                                                match state.ConnectionTestStatus with
                                                                | TestSuccess -> Theme.Brushes.accentGreen
                                                                | TestFailed _ -> Theme.Brushes.accentRed
                                                                | _ -> Theme.Brushes.textMuted
                                                            )
                                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                                            TextBlock.fontWeight FontWeight.Medium
                                                            TextBlock.textWrapping TextWrapping.Wrap
                                                            TextBlock.maxWidth 400.0
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        ]

                                        // Folders section
                                        TextBlock.create [
                                            TextBlock.text "Backup Folders"
                                            TextBlock.foreground Theme.Brushes.textSecondary
                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                            TextBlock.fontWeight FontWeight.Medium
                                            TextBlock.margin (Thickness(0.0, 0.0, 0.0, 12.0))
                                        ]

                                        // Folder list
                                        StackPanel.create [
                                            StackPanel.children [
                                                for (index, folder) in state.Config.LinuxServerFolders |> List.indexed do
                                                    createFolderRow index folder dispatch

                                                if state.Config.LinuxServerFolders.IsEmpty then
                                                    TextBlock.create [
                                                        TextBlock.text "No folders configured yet. Add a folder to start tracking Linux backups."
                                                        TextBlock.foreground Theme.Brushes.textMuted
                                                        TextBlock.fontSize Theme.Typography.fontSizeSm
                                                        TextBlock.fontStyle FontStyle.Italic
                                                        TextBlock.margin (Thickness(0.0, 0.0, 0.0, 12.0))
                                                    ]
                                            ]
                                        ]

                                        // Add folder form
                                        createNewFolderForm state dispatch
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
