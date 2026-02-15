namespace Savepoint.Services

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Savepoint.Domain

/// Configuration management service
module Config =

    /// JSON serialization options
    let private jsonOptions =
        let options = JsonSerializerOptions(WriteIndented = true)
        // Configure FSharp converter:
        // - SkippableOptionFields.Always: missing optional fields are treated as None
        let fsharpOptions =
            JsonFSharpOptions.Default()
                .WithSkippableOptionFields(SkippableOptionFields.Always)
        options.Converters.Add(JsonFSharpConverter(fsharpOptions))
        options.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Skip
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

    /// Recursively remove null values and unknown fields from JSON to handle migration
    let private cleanJsonForMigration (json: string) : string =
        let node = JsonNode.Parse(json)
        let rec removeNulls (n: JsonNode) =
            match n with
            | :? JsonObject as obj ->
                // Remove properties with null values (they will become None for option fields)
                let keysToRemove =
                    obj
                    |> Seq.filter (fun kvp -> kvp.Value = null || (kvp.Value :? JsonValue && kvp.Value.ToString() = "null"))
                    |> Seq.map (fun kvp -> kvp.Key)
                    |> Seq.toList
                for key in keysToRemove do
                    obj.Remove(key) |> ignore
                // Recursively process remaining properties
                for kvp in obj do
                    if kvp.Value <> null then
                        removeNulls kvp.Value
            | :? JsonArray as arr ->
                for item in arr do
                    if item <> null then
                        removeNulls item
            | _ -> ()
        removeNulls node
        node.ToJsonString()

    /// Load configuration from disk
    let load () : AppConfig =
        let configPath = getConfigPath ()
        if File.Exists(configPath) then
            try
                let json = File.ReadAllText(configPath)
                // Clean the JSON to handle migration from old configs
                let cleanedJson = cleanJsonForMigration json
                JsonSerializer.Deserialize<AppConfig>(cleanedJson, jsonOptions)
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
