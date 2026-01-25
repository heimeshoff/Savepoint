namespace Savepoint.Services

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Savepoint.Domain

/// Configuration management service
module Config =

    /// JSON serialization options
    let private jsonOptions =
        let options = JsonSerializerOptions(WriteIndented = true)
        options.Converters.Add(JsonFSharpConverter())
        options

    /// Get the configuration file path
    let getConfigPath () =
        let appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        let configDir = Path.Combine(appData, "Savepoint")
        Path.Combine(configDir, "config.json")

    /// Ensure the configuration directory exists
    let private ensureConfigDir () =
        let configPath = getConfigPath ()
        let configDir = Path.GetDirectoryName(configPath)
        if not (Directory.Exists(configDir)) then
            Directory.CreateDirectory(configDir) |> ignore

    /// Load configuration from disk
    let load () : AppConfig =
        let configPath = getConfigPath ()
        if File.Exists(configPath) then
            try
                let json = File.ReadAllText(configPath)
                JsonSerializer.Deserialize<AppConfig>(json, jsonOptions)
            with
            | ex ->
                printfn "Error loading config: %s" ex.Message
                defaultConfig
        else
            defaultConfig

    /// Save configuration to disk
    let save (config: AppConfig) : Result<unit, string> =
        try
            ensureConfigDir ()
            let configPath = getConfigPath ()
            let json = JsonSerializer.Serialize(config, jsonOptions)
            File.WriteAllText(configPath, json)
            Result.Ok ()
        with
        | ex -> Result.Error ex.Message

    /// Update a specific config field
    let update (updater: AppConfig -> AppConfig) : Result<AppConfig, string> =
        let current = load ()
        let updated = updater current
        match save updated with
        | Result.Ok () -> Result.Ok updated
        | Result.Error msg -> Result.Error msg
