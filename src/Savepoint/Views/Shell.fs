namespace Savepoint.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform.Storage
open Avalonia.Threading
open Savepoint
open Savepoint.Domain
open Savepoint.Services

/// Main shell view with sidebar navigation
module Shell =

    type State = {
        CurrentPage: Page
        Config: AppConfig
        DashboardState: Dashboard.State
        SettingsState: Settings.State
    }

    type Msg =
        | NavigateTo of Page
        | DashboardMsg of Dashboard.Msg
        | SettingsMsg of Settings.Msg
        | ConfigReloaded of AppConfig

    let init () =
        let config = Config.load ()
        let (settingsState, settingsCmd) = Settings.init ()
        let state = 
            { CurrentPage = Overview
              Config = config
              DashboardState = Dashboard.init config
              SettingsState = settingsState }
        let cmd = settingsCmd |> Elmish.Cmd.map SettingsMsg
        state, cmd

    let update (msg: Msg) (state: State) : State * Elmish.Cmd<Msg> =
        match msg with
        | NavigateTo page ->
            let newState = { state with CurrentPage = page }
            if page = Overview then
                { newState with DashboardState = Dashboard.init state.Config }, Elmish.Cmd.none
            else
                newState, Elmish.Cmd.none
        | DashboardMsg dashMsg ->
            let (newDashboardState, dashCmd) = Dashboard.update state.Config dashMsg state.DashboardState
            let shellCmd = dashCmd |> Elmish.Cmd.map DashboardMsg
            { state with DashboardState = newDashboardState }, shellCmd
        | SettingsMsg settingsMsg ->
            // Handle folder browser messages specially - need to show folder picker
            match settingsMsg with
            | Settings.BrowseLocalFolder ->
                let browseCmd : Elmish.Cmd<Msg> =
                    [ fun dispatch ->
                        async {
                            try
                                // Get the main window from the application
                                let app = Application.Current
                                match app.ApplicationLifetime with
                                | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                                    let window = desktop.MainWindow
                                    let storageProvider = window.StorageProvider
                                    let! folders =
                                        storageProvider.OpenFolderPickerAsync(
                                            FolderPickerOpenOptions(
                                                Title = "Select Local Folder",
                                                AllowMultiple = false
                                            )
                                        ) |> Async.AwaitTask
                                    if folders.Count > 0 then
                                        let folder = folders.[0]
                                        let path = folder.Path.LocalPath
                                        Dispatcher.UIThread.Post(fun () ->
                                            dispatch (SettingsMsg (Settings.LocalFolderSelected path)))
                                | _ -> ()
                            with _ -> ()
                        } |> Async.Start
                    ]
                state, browseCmd
            | Settings.BrowseForPath field ->
                let browseCmd : Elmish.Cmd<Msg> =
                    [ fun dispatch ->
                        async {
                            try
                                let app = Application.Current
                                match app.ApplicationLifetime with
                                | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                                    let window = desktop.MainWindow
                                    let storageProvider = window.StorageProvider
                                    let! folders =
                                        storageProvider.OpenFolderPickerAsync(
                                            FolderPickerOpenOptions(
                                                Title = "Select Folder",
                                                AllowMultiple = false
                                            )
                                        ) |> Async.AwaitTask
                                    if folders.Count > 0 then
                                        let folder = folders.[0]
                                        let path = folder.Path.LocalPath
                                        Dispatcher.UIThread.Post(fun () ->
                                            dispatch (SettingsMsg (Settings.PathSelected (field, path))))
                                | _ -> ()
                            with _ -> ()
                        } |> Async.Start
                    ]
                state, browseCmd
            | _ ->
                let (newSettingsState, settingsCmd) = Settings.update settingsMsg state.SettingsState
                let newConfig =
                    match settingsMsg with
                    | Settings.Save -> Config.load ()
                    | _ -> state.Config
                let shellCmd = settingsCmd |> Elmish.Cmd.map SettingsMsg
                { state with
                    SettingsState = newSettingsState
                    Config = newConfig }, shellCmd
        | ConfigReloaded config ->
            { state with Config = config }, Elmish.Cmd.none

    /// Create a navigation item
    let private navItem (icon: string) (label: string) (page: Page) (currentPage: Page) (dispatch: Msg -> unit) =
        let isActive = page = currentPage
        Button.create [
            Button.content (
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 12.0
                    StackPanel.children [
                        TextBlock.create [
                            TextBlock.text icon
                            TextBlock.fontSize 18.0
                            TextBlock.fontWeight (if isActive then FontWeight.Bold else FontWeight.Normal)
                            TextBlock.foreground (if isActive then Theme.Brushes.primary else Theme.Brushes.textMuted)
                        ]
                        TextBlock.create [
                            TextBlock.text label
                            TextBlock.fontSize Theme.Typography.fontSizeSm
                            TextBlock.fontWeight FontWeight.Medium
                            TextBlock.foreground (if isActive then Theme.Brushes.primary else Theme.Brushes.textMuted)
                        ]
                    ]
                ]
            )
            Button.horizontalAlignment HorizontalAlignment.Stretch
            Button.horizontalContentAlignment HorizontalAlignment.Left
            Button.padding (Thickness(12.0, 10.0, 12.0, 10.0))
            Button.background (
                if isActive then
                    SolidColorBrush(Color.FromArgb(byte 38, Theme.primary.R, Theme.primary.G, Theme.primary.B))
                else
                    Theme.Brushes.transparent
            )
            Button.cornerRadius 8.0
            Button.onClick (fun _ -> dispatch (NavigateTo page))
        ]

    /// Create the sidebar
    let private sidebar (currentPage: Page) (dispatch: Msg -> unit) =
        Border.create [
            Border.width Theme.Sizes.sidebarWidth
            Border.background (SolidColorBrush(Color.FromArgb(byte 217, Theme.background.R, Theme.background.G, Theme.background.B)))
            Border.borderBrush Theme.Brushes.transparent
            Border.borderThickness 0.0
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        // Logo/Title at top
                        Border.create [
                            DockPanel.dock Dock.Top
                            Border.padding 24.0
                            Border.child (
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.children [
                                        // Logo icon
                                        Border.create [
                                            Border.padding 8.0
                                            Border.cornerRadius 8.0
                                            Border.background (SolidColorBrush(Color.FromArgb(byte 51, Theme.primary.R, Theme.primary.G, Theme.primary.B)))
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text "S"
                                                    TextBlock.foreground Theme.Brushes.primary
                                                    TextBlock.fontSize 18.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                            )
                                        ]
                                        TextBlock.create [
                                            TextBlock.text "Savepoint"
                                            TextBlock.foreground Theme.Brushes.textPrimary
                                            TextBlock.fontSize Theme.Typography.fontSizeXl
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                    ]
                                ]
                            )
                        ]

                        // Settings at bottom
                        Border.create [
                            DockPanel.dock Dock.Bottom
                            Border.padding (Thickness(16.0, 8.0, 16.0, 16.0))
                            Border.child (navItem "S" "Settings" Page.Settings currentPage dispatch)
                        ]

                        // Navigation items
                        StackPanel.create [
                            StackPanel.margin (Thickness(16.0, 8.0, 16.0, 8.0))
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                navItem "O" "Overview" Overview currentPage dispatch
                                navItem "F" "Sources" Sources currentPage dispatch
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// Create the header bar
    let private header () =
        Border.create [
            Border.height Theme.Sizes.headerHeight
            Border.background (SolidColorBrush(Color.FromArgb(byte 128, Theme.background.R, Theme.background.G, Theme.background.B)))
            Border.borderBrush Theme.Brushes.transparent
            Border.borderThickness 0.0
            Border.padding (Thickness(32.0, 0.0, 32.0, 0.0))
            Border.child (
                DockPanel.create [
                    DockPanel.verticalAlignment VerticalAlignment.Center
                    DockPanel.children [
                        // Right side: user avatar placeholder
                        StackPanel.create [
                            DockPanel.dock Dock.Right
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 16.0
                            StackPanel.children [
                                // User avatar placeholder
                                Border.create [
                                    Border.width 32.0
                                    Border.height 32.0
                                    Border.cornerRadius 16.0
                                    Border.background (
                                        let gradient = LinearGradientBrush()
                                        gradient.StartPoint <- RelativePoint(0.0, 0.0, RelativeUnit.Relative)
                                        gradient.EndPoint <- RelativePoint(1.0, 1.0, RelativeUnit.Relative)
                                        gradient.GradientStops.Add(GradientStop(Theme.primary, 0.0))
                                        gradient.GradientStops.Add(GradientStop(Theme.accentPink, 1.0))
                                        gradient
                                    )
                                ]
                            ]
                        ]

                        // Left side: date and status
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 16.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text (DateTime.Now.ToString("dddd, MMM dd"))
                                    TextBlock.foreground Theme.Brushes.textMuted
                                    TextBlock.fontSize Theme.Typography.fontSizeSm
                                    TextBlock.fontWeight FontWeight.Medium
                                ]
                                Border.create [
                                    Border.width 1.0
                                    Border.height 16.0
                                    Border.background Theme.Brushes.border
                                ]
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children [
                                        Border.create [
                                            Border.width 8.0
                                            Border.height 8.0
                                            Border.cornerRadius 4.0
                                            Border.background Theme.Brushes.accentGreen
                                        ]
                                        TextBlock.create [
                                            TextBlock.text "System Online"
                                            TextBlock.foreground Theme.Brushes.textSecondary
                                            TextBlock.fontSize Theme.Typography.fontSizeSm
                                            TextBlock.fontWeight FontWeight.Medium
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// Render the current page content
    let private renderPage (state: State) (dispatch: Msg -> unit) : IView =
        match state.CurrentPage with
        | Overview ->
            Dashboard.view state.DashboardState (DashboardMsg >> dispatch) :> IView
        | Sources ->
            Border.create [
                Border.padding 32.0
                Border.child (
                    StackPanel.create [
                        StackPanel.children [
                            TextBlock.create [
                                TextBlock.text "Sources"
                                TextBlock.foreground Theme.Brushes.textPrimary
                                TextBlock.fontSize Theme.Typography.fontSizeXxl
                                TextBlock.fontWeight FontWeight.Bold
                            ]
                            TextBlock.create [
                                TextBlock.text "Detailed source management coming in a future update."
                                TextBlock.foreground Theme.Brushes.textMuted
                                TextBlock.fontSize Theme.Typography.fontSizeMd
                                TextBlock.margin (Thickness(0.0, 8.0, 0.0, 0.0))
                            ]
                        ]
                    ]
                )
            ] :> IView
        | Page.Settings ->
            Settings.view state.SettingsState (SettingsMsg >> dispatch) :> IView

    let view (state: State) (dispatch: Msg -> unit) =
        DockPanel.create [
            DockPanel.background Theme.Brushes.background
            DockPanel.children [
                // Sidebar (left)
                Border.create [
                    DockPanel.dock Dock.Left
                    Border.child (sidebar state.CurrentPage dispatch)
                ]

                // Main content area
                DockPanel.create [
                    DockPanel.children [
                        // Header (top)
                        Border.create [
                            DockPanel.dock Dock.Top
                            Border.child (header ())
                        ]

                        // Page content
                        Border.create [
                            Border.padding 32.0
                            Border.child (renderPage state dispatch)
                        ]
                    ]
                ]
            ]
        ]
