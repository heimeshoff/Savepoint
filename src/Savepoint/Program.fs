namespace Savepoint

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI
open Avalonia.FuncUI.Hosts
open Avalonia.Themes.Fluent
open Savepoint.Views

/// Main application entry point
module Program =

    /// The main window host
    type MainWindow() as this =
        inherit HostWindow()

        do
            base.Title <- "Savepoint - Backup Dashboard"
            base.Width <- 1400.0
            base.Height <- 900.0
            base.MinWidth <- 1024.0
            base.MinHeight <- 768.0

            // Set window background
            this.Background <- Theme.Brushes.background

            // Initialize the Elmish program with command support
            Elmish.Program.mkProgram Shell.init Shell.update Shell.view
            |> Elmish.Program.withHost this
            |> Elmish.Program.run

    /// Application class
    type App() =
        inherit Application()

        override this.Initialize() =
            // Use Fluent theme with dark mode
            this.Styles.Add(FluentTheme())
            this.RequestedThemeVariant <- Styling.ThemeVariant.Dark

        override this.OnFrameworkInitializationCompleted() =
            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                desktop.MainWindow <- MainWindow()
            | _ -> ()
            base.OnFrameworkInitializationCompleted()

    /// Application entry point
    [<EntryPoint>]
    let main (args: string[]) =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .StartWithClassicDesktopLifetime(args)
