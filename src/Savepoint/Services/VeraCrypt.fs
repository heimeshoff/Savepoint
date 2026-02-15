namespace Savepoint.Services

open System
open System.Diagnostics
open System.IO
open Savepoint.Domain

/// Service for managing VeraCrypt volume mounting and unmounting
module VeraCrypt =

    /// Check if a drive letter is currently mounted
    let isMounted (driveLetter: char) : bool =
        let drivePath = sprintf "%c:\\" driveLetter
        Directory.Exists(drivePath)

    /// Get the current status of a VeraCrypt configuration
    let getStatus (config: VeraCryptConfig) : VeraCryptStatus =
        match config.ExePath, config.PartitionDevicePath with
        | None, _ | _, None -> VCNotConfigured
        | Some exePath, Some _ ->
            if not (File.Exists(exePath)) then
                VeraCryptError (sprintf "VeraCrypt executable not found: %s" exePath)
            elif isMounted config.MountLetter then
                Mounted config.MountLetter
            else
                Unmounted

    /// Run a VeraCrypt command and capture output
    let private runVeraCrypt (exePath: string) (args: string) : Async<Result<string, string>> =
        async {
            try
                let startInfo = ProcessStartInfo(
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                )

                use proc = new Process()
                proc.StartInfo <- startInfo

                let output = System.Text.StringBuilder()
                let error = System.Text.StringBuilder()

                proc.OutputDataReceived.Add(fun e ->
                    if not (isNull e.Data) then
                        output.AppendLine(e.Data) |> ignore
                )
                proc.ErrorDataReceived.Add(fun e ->
                    if not (isNull e.Data) then
                        error.AppendLine(e.Data) |> ignore
                )

                proc.Start() |> ignore
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                // Wait for the process with a timeout (30 seconds for mount operations)
                let! completed = Async.AwaitTask(proc.WaitForExitAsync())

                if proc.ExitCode = 0 then
                    return Result.Ok (output.ToString())
                else
                    let errMsg = error.ToString()
                    if String.IsNullOrWhiteSpace(errMsg) then
                        return Result.Error (sprintf "VeraCrypt exited with code %d" proc.ExitCode)
                    else
                        return Result.Error (errMsg.Trim())
            with ex ->
                return Result.Error ex.Message
        }

    /// Mount a VeraCrypt partition
    /// CLI: veracrypt.exe /volume "device_path" /letter X /password "pwd" /quit /silent
    let mount (config: VeraCryptConfig) (password: string) : Async<Result<char, string>> =
        async {
            match config.ExePath, config.PartitionDevicePath with
            | None, _ -> return Result.Error "VeraCrypt executable path not configured"
            | _, None -> return Result.Error "Partition not configured"
            | Some exePath, Some partitionPath ->
                if not (File.Exists(exePath)) then
                    return Result.Error (sprintf "VeraCrypt executable not found: %s" exePath)
                elif isMounted config.MountLetter then
                    return Result.Ok config.MountLetter  // Already mounted
                else
                    // Build the mount command
                    // /quit - auto-close GUI after operation
                    // /silent - suppress UI dialogs
                    // /nowaitdlg - don't show wait dialog
                    let args = sprintf "/volume \"%s\" /letter %c /password \"%s\" /quit /silent /nowaitdlg"
                                    partitionPath config.MountLetter password

                    let! result = runVeraCrypt exePath args

                    match result with
                    | Result.Error err -> return Result.Error err
                    | Result.Ok _ ->
                        // Wait a moment for the drive to become available
                        do! Async.Sleep 1000

                        // Verify mount succeeded
                        if isMounted config.MountLetter then
                            return Result.Ok config.MountLetter
                        else
                            return Result.Error "Mount command succeeded but drive not available"
        }

    /// Dismount a VeraCrypt volume
    /// CLI: veracrypt.exe /dismount X /quit /silent /force
    let dismount (config: VeraCryptConfig) : Async<Result<unit, string>> =
        async {
            match config.ExePath with
            | None -> return Result.Error "VeraCrypt executable path not configured"
            | Some exePath ->
                if not (File.Exists(exePath)) then
                    return Result.Error (sprintf "VeraCrypt executable not found: %s" exePath)
                elif not (isMounted config.MountLetter) then
                    return Result.Ok ()  // Already unmounted
                else
                    // /force - force dismount even if files are open
                    let args = sprintf "/dismount %c /quit /silent /force" config.MountLetter

                    let! result = runVeraCrypt exePath args

                    match result with
                    | Result.Error err -> return Result.Error err
                    | Result.Ok _ ->
                        // Wait a moment for the drive to become unavailable
                        do! Async.Sleep 500

                        // Verify dismount succeeded
                        if not (isMounted config.MountLetter) then
                            return Result.Ok ()
                        else
                            return Result.Error "Dismount command succeeded but drive still available"
        }

    /// Default VeraCrypt configuration
    let defaultConfig: VeraCryptConfig = {
        ExePath = None
        PartitionDevicePath = None
        MountLetter = 'B'
    }
