namespace Savepoint.Services

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open Savepoint.Domain

/// Service for running Robocopy file synchronization
module Robocopy =

    /// Build Robocopy command-line arguments
    /// Flags: /MIR (mirror) /MT:8 (8 threads) /R:1 (1 retry) /W:1 (1 sec wait) /BYTES (show bytes) /V (verbose)
    /// Dry run adds /L (list only)
    let buildArgs (config: RobocopyConfig) (isDryRun: bool) : string =
        // Remove trailing backslashes to avoid issues with quoted paths
        // (a trailing backslash before a quote can be interpreted as an escaped quote)
        let source = config.SourcePath.TrimEnd('\\')
        let dest = config.DestinationPath.TrimEnd('\\')
        // /FFT: Assume FAT file times (2-second granularity) to handle NTFS/FAT32 timestamp precision differences
        let baseArgs = sprintf "\"%s\" \"%s\" /MIR /MT:8 /R:1 /W:1 /FFT /BYTES /NP /NDL /NJH"
                           source dest
        if isDryRun then
            baseArgs + " /L"
        else
            baseArgs

    /// Parse the summary section from Robocopy output
    /// Example output:
    ///     Total    Copied   Skipped  Mismatch    FAILED    Extras
    ///    Files :      1234       123      1111         0         0         0
    ///    Bytes :   1.234 g   123.4 m   1.111 g         0         0         0
    let parseSummary (output: string) : RobocopySummary option =
        try
            // Parse Files line
            let filesPattern = @"Files\s*:\s*(\d+)\s+(\d+)\s+(\d+)\s+\d+\s+(\d+)"
            let filesMatch = Regex.Match(output, filesPattern)

            // Parse Bytes line - handles various formats (b, k, m, g suffixes)
            let bytesPattern = @"Bytes\s*:\s*([\d.]+\s*[bkmg]?)\s+([\d.]+\s*[bkmg]?)"
            let bytesMatch = Regex.Match(output, bytesPattern, RegexOptions.IgnoreCase)

            let parseByteValue (s: string) : int64 =
                let s = s.Trim().ToLowerInvariant()
                if String.IsNullOrWhiteSpace(s) || s = "0" then 0L
                else
                    let numPart = Regex.Match(s, @"[\d.]+")
                    if numPart.Success then
                        let num = Double.Parse(numPart.Value, System.Globalization.CultureInfo.InvariantCulture)
                        if s.EndsWith("g") then int64 (num * 1024.0 * 1024.0 * 1024.0)
                        elif s.EndsWith("m") then int64 (num * 1024.0 * 1024.0)
                        elif s.EndsWith("k") then int64 (num * 1024.0)
                        else int64 num
                    else 0L

            if filesMatch.Success then
                let filesTotal = Int32.Parse(filesMatch.Groups.[1].Value)
                let filesCopied = Int32.Parse(filesMatch.Groups.[2].Value)
                let filesSkipped = Int32.Parse(filesMatch.Groups.[3].Value)
                let filesFailed = Int32.Parse(filesMatch.Groups.[4].Value)

                let (bytesTotal, bytesCopied) =
                    if bytesMatch.Success then
                        (parseByteValue bytesMatch.Groups.[1].Value,
                         parseByteValue bytesMatch.Groups.[2].Value)
                    else
                        (0L, 0L)

                Some {
                    FilesTotal = filesTotal
                    FilesCopied = filesCopied
                    FilesSkipped = filesSkipped
                    FilesFailed = filesFailed
                    BytesTotal = bytesTotal
                    BytesCopied = bytesCopied
                }
            else
                None
        with _ ->
            None

    /// Parse file operation details from Robocopy output line
    /// Example: "        New File                 12345        C:\path\to\file.txt"
    /// Returns structured FileEntry with operation type, path, and size
    let private fileOperationRegex =
        Regex(@"^\s*(New File|\*?EXTRA File|Extra File|Newer|Older)\s+(\d+)\s+(.+)$", RegexOptions.Compiled)

    let parseFileOperation (line: string) : FileEntry option =
        if String.IsNullOrWhiteSpace(line) then None
        else
            let m = fileOperationRegex.Match(line)
            if m.Success then
                let tag = m.Groups.[1].Value
                let operation =
                    if tag = "New File" then NewFile
                    elif tag = "Newer" then Newer
                    elif tag = "Older" then Older
                    else ExtraFile

                let sizeStr = m.Groups.[2].Value
                let fullPath = m.Groups.[3].Value.Trim()
                let fileName = Path.GetFileName(fullPath)

                let sizeOpt =
                    match Int64.TryParse(sizeStr) with
                    | true, v -> Some v
                    | false, _ -> None

                Some {
                    Operation = operation
                    FullPath = fullPath
                    FileName = fileName
                    FileSize = sizeOpt
                }
            else
                None

    /// Robocopy exit codes:
    /// 0 = No files copied, no errors
    /// 1 = Files copied successfully
    /// 2 = Extra files or directories detected
    /// 4 = Mismatched files or directories detected
    /// 8 = Some files or directories could not be copied
    /// 16 = Fatal error
    /// Codes 0-7 are considered success, 8+ are errors
    let private isSuccessExitCode (exitCode: int) =
        exitCode >= 0 && exitCode < 8

    /// Run Robocopy synchronization with progress callback
    let runSync
        (config: RobocopyConfig)
        (isDryRun: bool)
        (onProgress: RobocopyProgressInfo -> unit)
        (onFileOperation: FileEntry -> unit)
        : Async<Result<RobocopySummary, string>> =
        async {
            if not (Directory.Exists(config.SourcePath)) then
                return Result.Error (sprintf "Source path not found: %s" config.SourcePath)
            // Check if destination drive exists (e.g., VeraCrypt might not be mounted)
            elif not (Directory.Exists(Path.GetPathRoot(config.DestinationPath))) then
                return Result.Error (sprintf "Destination drive not accessible: %s (is VeraCrypt mounted?)" config.DestinationPath)
            else
                try
                    let args = buildArgs config isDryRun
                    let startInfo = ProcessStartInfo(
                        FileName = "robocopy",
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    )

                    use proc = new Process()
                    proc.StartInfo <- startInfo

                    let output = System.Text.StringBuilder()
                    let parsedEntries = System.Collections.Generic.List<FileEntry>()
                    let mutable filesProcessed = 0
                    let mutable lastProgressUpdate = DateTime.Now

                    proc.OutputDataReceived.Add(fun e ->
                        if not (isNull e.Data) then
                            output.AppendLine(e.Data) |> ignore

                            // Try to parse file operation (structured)
                            match parseFileOperation e.Data with
                            | Some fileEntry ->
                                filesProcessed <- filesProcessed + 1
                                parsedEntries.Add(fileEntry)
                                onFileOperation fileEntry
                                // Throttle progress updates to max ~7/sec to avoid flooding UI thread
                                let now = DateTime.Now
                                if (now - lastProgressUpdate).TotalMilliseconds >= 150.0 then
                                    lastProgressUpdate <- now
                                    onProgress {
                                        CurrentFile = fileEntry.FileName
                                        FilesProcessed = filesProcessed
                                        OverallPercent = 0
                                        IsDryRun = isDryRun
                                    }
                            | None -> ()
                    )

                    proc.ErrorDataReceived.Add(fun e ->
                        if not (isNull e.Data) then
                            output.AppendLine(e.Data) |> ignore
                    )

                    proc.Start() |> ignore
                    proc.BeginOutputReadLine()
                    proc.BeginErrorReadLine()

                    do! Async.AwaitTask(proc.WaitForExitAsync())

                    let fullOutput = output.ToString()

                    // Send a final progress update so the UI reflects the final count
                    if filesProcessed > 0 then
                        onProgress {
                            CurrentFile = ""
                            FilesProcessed = filesProcessed
                            OverallPercent = 100
                            IsDryRun = isDryRun
                        }

                    // Diagnostic: write raw output and parsed entries to log file
                    try
                        let logPath = Path.Combine(Path.GetTempPath(), "savepoint_robocopy_debug.log")
                        let logContent = System.Text.StringBuilder()
                        logContent.AppendLine("=== ROBOCOPY VERBATIM OUTPUT ===") |> ignore
                        logContent.AppendLine(fullOutput) |> ignore
                        logContent.AppendLine("=== PARSED FILE ENTRIES (FullPath values) ===") |> ignore
                        for entry in parsedEntries do
                            logContent.AppendLine(sprintf "[%A] %s" entry.Operation entry.FullPath) |> ignore
                        logContent.AppendLine(sprintf "=== TOTAL PARSED: %d entries ===" parsedEntries.Count) |> ignore
                        File.WriteAllText(logPath, logContent.ToString())
                    with _ -> ()

                    if isSuccessExitCode proc.ExitCode then
                        match parseSummary fullOutput with
                        | Some summary -> return Result.Ok summary
                        | None ->
                            // Return a default summary if parsing failed but exit was successful
                            return Result.Ok {
                                FilesTotal = filesProcessed
                                FilesCopied = filesProcessed
                                FilesSkipped = 0
                                FilesFailed = 0
                                BytesTotal = 0L
                                BytesCopied = 0L
                            }
                    else
                        return Result.Error (sprintf "Robocopy failed with exit code %d" proc.ExitCode)

                with ex ->
                    return Result.Error ex.Message
        }

    /// Check if Robocopy configuration is valid
    let isConfigured (config: RobocopyConfig option) : bool =
        match config with
        | None -> false
        | Some cfg ->
            not (String.IsNullOrWhiteSpace(cfg.SourcePath)) &&
            not (String.IsNullOrWhiteSpace(cfg.DestinationPath))

    /// Default Robocopy configuration
    let defaultConfig: RobocopyConfig = {
        SourcePath = ""
        DestinationPath = ""
    }
