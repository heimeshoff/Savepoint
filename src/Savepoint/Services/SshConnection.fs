namespace Savepoint.Services

open System
open System.IO
open System.Threading.Tasks
open Renci.SshNet

/// SSH/SCP connection service for Linux server backups
module SshConnection =

    /// Log file path in AppData
    let private logFilePath =
        let appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        let savepointDir = Path.Combine(appData, "Savepoint")
        if not (Directory.Exists(savepointDir)) then
            Directory.CreateDirectory(savepointDir) |> ignore
        Path.Combine(savepointDir, "ssh.log")

    /// Write a log message with timestamp
    let private log (message: string) =
        let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        let line = sprintf "[%s] %s" timestamp message
        try
            File.AppendAllText(logFilePath, line + Environment.NewLine)
        with _ -> ()  // Ignore logging errors

    /// Clear the log file (call at start of connection test)
    let private clearLog () =
        try
            File.WriteAllText(logFilePath, "")
        with _ -> ()

    /// Connection status for the Linux server
    type ConnectionStatus =
        | Connected
        | Disconnected
        | Error of string

    /// SSH credentials (passphrase is runtime-only, not stored)
    type SshCredentials = {
        Host: string
        Port: int
        Username: string
        KeyPath: string
        Passphrase: string option
    }

    /// Create SSH connection info from credentials
    let private createConnectionInfo (creds: SshCredentials) : ConnectionInfo =
        log (sprintf "Creating auth method for user: %s" creds.Username)
        let authMethod =
            match creds.Passphrase with
            | Some passphrase ->
                log "Loading private key WITH passphrase..."
                let keyFile = new PrivateKeyFile(creds.KeyPath, passphrase)
                log "Private key loaded (with passphrase)"
                new PrivateKeyAuthenticationMethod(
                    creds.Username,
                    keyFile
                ) :> AuthenticationMethod
            | None ->
                log "Loading private key WITHOUT passphrase..."
                let keyFile = new PrivateKeyFile(creds.KeyPath)
                log "Private key loaded (no passphrase)"
                new PrivateKeyAuthenticationMethod(
                    creds.Username,
                    keyFile
                ) :> AuthenticationMethod
        log (sprintf "Creating ConnectionInfo for %s:%d" creds.Host creds.Port)
        ConnectionInfo(creds.Host, creds.Port, creds.Username, authMethod)

    /// Test if the server is reachable (for status indicator)
    let testConnection (creds: SshCredentials) : Async<ConnectionStatus> =
        async {
            try
                clearLog ()  // Start fresh log for each test
                log (sprintf "Starting connection test to %s:%d as %s" creds.Host creds.Port creds.Username)
                log (sprintf "Log file: %s" logFilePath)

                // Validate key file exists first
                if not (File.Exists(creds.KeyPath)) then
                    log (sprintf "ERROR: Key file not found: %s" creds.KeyPath)
                    return Error (sprintf "SSH key file not found: %s" creds.KeyPath)
                else
                    log (sprintf "Key file exists: %s" creds.KeyPath)

                    log "Loading private key..."
                    let connectionInfo = createConnectionInfo creds
                    log "Private key loaded successfully"

                    log "Creating SSH client..."
                    use client = new SshClient(connectionInfo)
                    client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(10.0)
                    log "Timeout set to 10 seconds"

                    log "Attempting to connect..."
                    client.Connect()
                    log (sprintf "Connect() returned, IsConnected=%b" client.IsConnected)

                    let result = if client.IsConnected then Connected else Disconnected
                    client.Disconnect()
                    log "Disconnected, test complete"
                    return result
            with
            | :? Renci.SshNet.Common.SshAuthenticationException as ex ->
                log (sprintf "Authentication exception: %s" ex.Message)
                return Error (sprintf "Authentication failed: %s" ex.Message)
            | :? Renci.SshNet.Common.SshConnectionException as ex ->
                log (sprintf "Connection exception: %s" ex.Message)
                return Error (sprintf "Connection failed: %s" ex.Message)
            | :? System.Net.Sockets.SocketException as ex ->
                log (sprintf "Socket exception: %s" ex.Message)
                return Error (sprintf "Network error: %s" ex.Message)
            | :? Renci.SshNet.Common.SshPassPhraseNullOrEmptyException as ex ->
                log (sprintf "Passphrase required but not provided: %s" ex.Message)
                return Error "SSH key requires a passphrase"
            | ex ->
                log (sprintf "Unexpected exception (%s): %s" (ex.GetType().Name) ex.Message)
                return Error (sprintf "SSH error: %s" ex.Message)
        }

    /// Test connection as a Task (for better Elmish compatibility)
    let testConnectionTask (creds: SshCredentials) : Task<ConnectionStatus> =
        testConnection creds |> Async.StartAsTask

    /// Download a file via SCP with progress callback (legacy - single file)
    let downloadFile (creds: SshCredentials) (remotePath: string) (localPath: string) (onProgress: int64 -> unit) : Async<Result<unit, string>> =
        async {
            try
                // Ensure local directory exists
                let localDir = Path.GetDirectoryName(localPath)
                if not (Directory.Exists(localDir)) then
                    Directory.CreateDirectory(localDir) |> ignore

                let connectionInfo = createConnectionInfo creds
                use client = new ScpClient(connectionInfo)
                client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(30.0)
                client.Connect()

                if not client.IsConnected then
                    return Result.Error "Failed to connect to server"
                else
                    // Download file
                    use fileStream = File.Create(localPath)
                    client.Download(remotePath, fileStream)

                    // Report final size
                    onProgress fileStream.Length

                    client.Disconnect()
                    return Result.Ok ()
            with
            | :? Renci.SshNet.Common.SshAuthenticationException as ex ->
                return Result.Error (sprintf "Authentication failed: %s" ex.Message)
            | :? Renci.SshNet.Common.ScpException as ex ->
                return Result.Error (sprintf "SCP error: %s" ex.Message)
            | :? System.Net.Sockets.SocketException as ex ->
                return Result.Error (sprintf "Network error: %s" ex.Message)
            | ex ->
                return Result.Error (sprintf "Download error: %s" ex.Message)
        }

    /// File info with size for progress tracking
    type RemoteFileInfo = {
        FullPath: string
        RelativePath: string
        Size: int64
    }

    /// Progress callback for bulk downloads
    type BulkDownloadProgress = {
        CurrentFileIndex: int
        TotalFiles: int
        CurrentFileName: string
        BytesDownloadedTotal: int64
        TotalBytes: int64
    }

    /// Download multiple files using a single SFTP connection (much faster)
    let downloadFilesBulk
        (creds: SshCredentials)
        (files: RemoteFileInfo list)
        (localBasePath: string)
        (onProgress: BulkDownloadProgress -> unit)
        : Async<Result<int * int, string>> = // Returns (success count, fail count)
        async {
            try
                let connectionInfo = createConnectionInfo creds
                use client = new Renci.SshNet.SftpClient(connectionInfo)
                client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(30.0)
                client.OperationTimeout <- TimeSpan.FromMinutes(5.0)
                client.BufferSize <- 32768u  // 32KB buffer for better performance
                client.Connect()

                if not client.IsConnected then
                    return Result.Error "Failed to connect to server"
                else
                    let totalBytes = files |> List.sumBy (fun f -> f.Size)
                    let mutable bytesDownloaded = 0L
                    let mutable successCount = 0
                    let mutable failCount = 0
                    let totalFiles = files.Length

                    for (idx, fileInfo) in files |> List.indexed do
                        try
                            // Build local path
                            let localRelativePath = fileInfo.RelativePath.Replace('/', Path.DirectorySeparatorChar)
                            let localPath = Path.Combine(localBasePath, localRelativePath)

                            // Ensure directory exists
                            let localDir = Path.GetDirectoryName(localPath)
                            if not (String.IsNullOrEmpty(localDir)) && not (Directory.Exists(localDir)) then
                                Directory.CreateDirectory(localDir) |> ignore

                            // Report progress before download
                            onProgress {
                                CurrentFileIndex = idx + 1
                                TotalFiles = totalFiles
                                CurrentFileName = fileInfo.RelativePath
                                BytesDownloadedTotal = bytesDownloaded
                                TotalBytes = totalBytes
                            }

                            // Download file
                            use fileStream = File.Create(localPath)
                            client.DownloadFile(fileInfo.FullPath, fileStream)
                            bytesDownloaded <- bytesDownloaded + fileInfo.Size
                            successCount <- successCount + 1
                        with
                        | _ -> failCount <- failCount + 1

                    client.Disconnect()
                    return Result.Ok (successCount, failCount)
            with
            | :? Renci.SshNet.Common.SshAuthenticationException as ex ->
                return Result.Error (sprintf "Authentication failed: %s" ex.Message)
            | :? System.Net.Sockets.SocketException as ex ->
                return Result.Error (sprintf "Network error: %s" ex.Message)
            | ex ->
                return Result.Error (sprintf "Download error: %s" ex.Message)
        }

    /// List files recursively with their sizes (for progress tracking)
    let listRemoteFilesWithSizes (creds: SshCredentials) (remotePath: string) : Async<Result<RemoteFileInfo list, string>> =
        async {
            try
                let connectionInfo = createConnectionInfo creds
                use client = new Renci.SshNet.SftpClient(connectionInfo)
                client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(30.0)
                client.Connect()

                if not client.IsConnected then
                    return Result.Error "Failed to connect to server"
                else
                    let basePath = remotePath.TrimEnd('/')
                    let results = ResizeArray<RemoteFileInfo>()

                    // Recursive function to list files
                    let rec listDir (path: string) =
                        try
                            for entry in client.ListDirectory(path) do
                                if entry.Name <> "." && entry.Name <> ".." then
                                    if entry.IsDirectory then
                                        listDir entry.FullName
                                    elif entry.IsRegularFile then
                                        let relativePath =
                                            if entry.FullName.StartsWith(basePath + "/") then
                                                entry.FullName.Substring(basePath.Length + 1)
                                            else
                                                entry.Name
                                        results.Add({
                                            FullPath = entry.FullName
                                            RelativePath = relativePath
                                            Size = entry.Length
                                        })
                        with _ -> ()

                    listDir basePath
                    client.Disconnect()
                    return Result.Ok (results |> Seq.toList)
            with
            | ex ->
                return Result.Error (sprintf "List files error: %s" ex.Message)
        }

    /// Remote file entry with full path and relative path for folder structure preservation
    type RemoteFileEntry = {
        FullPath: string      // Full path on remote server
        RelativePath: string  // Path relative to the base folder (for local structure)
    }

    /// List files recursively in a remote directory (excludes directories)
    let listRemoteFilesRecursive (creds: SshCredentials) (remotePath: string) : Async<Result<RemoteFileEntry list, string>> =
        async {
            try
                let connectionInfo = createConnectionInfo creds
                use client = new SshClient(connectionInfo)
                client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(30.0)
                client.Connect()

                if not client.IsConnected then
                    return Result.Error "Failed to connect to server"
                else
                    // Use find command to list only files (not directories) recursively
                    // -type f = only files, not directories
                    let basePath = remotePath.TrimEnd('/')
                    let command = sprintf "find '%s' -type f 2>/dev/null || true" basePath
                    use cmd = client.RunCommand(command)

                    let files =
                        cmd.Result.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.choose (fun fullPath ->
                            let fullPath = fullPath.Trim()
                            if String.IsNullOrWhiteSpace(fullPath) then
                                None
                            else
                                // Calculate relative path from base
                                let relativePath =
                                    if fullPath.StartsWith(basePath + "/") then
                                        fullPath.Substring(basePath.Length + 1)
                                    elif fullPath = basePath then
                                        Path.GetFileName(fullPath)
                                    else
                                        Path.GetFileName(fullPath)
                                Some {
                                    FullPath = fullPath
                                    RelativePath = relativePath
                                }
                        )
                        |> Array.toList

                    client.Disconnect()
                    return Result.Ok files
            with
            | ex ->
                return Result.Error (sprintf "List files error: %s" ex.Message)
        }

    /// List files in a remote directory matching a pattern (non-recursive, files only)
    let listRemoteFiles (creds: SshCredentials) (remotePath: string) (pattern: string) : Async<Result<string list, string>> =
        async {
            try
                let connectionInfo = createConnectionInfo creds
                use client = new SshClient(connectionInfo)
                client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(10.0)
                client.Connect()

                if not client.IsConnected then
                    return Result.Error "Failed to connect to server"
                else
                    // Use find with maxdepth 1 to list only files (not directories) in this folder
                    let basePath = remotePath.TrimEnd('/')
                    let command =
                        if pattern = "*" then
                            sprintf "find '%s' -maxdepth 1 -type f 2>/dev/null || true" basePath
                        else
                            sprintf "find '%s' -maxdepth 1 -type f -name '%s' 2>/dev/null || true" basePath pattern
                    use cmd = client.RunCommand(command)

                    let files =
                        cmd.Result.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun s -> s.Trim())
                        |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace(s)))
                        |> Array.toList

                    client.Disconnect()
                    return Result.Ok files
            with
            | ex ->
                return Result.Error (sprintf "List files error: %s" ex.Message)
        }

    /// Directory entry from remote listing
    type DirectoryEntry = {
        Name: string
        FullPath: string
        IsDirectory: bool
    }

    /// List directories in a remote path
    let listDirectories (creds: SshCredentials) (remotePath: string) : Async<Result<DirectoryEntry list, string>> =
        async {
            try
                let connectionInfo = createConnectionInfo creds
                use client = new SshClient(connectionInfo)
                client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(10.0)
                client.Connect()

                if not client.IsConnected then
                    return Result.Error "Failed to connect to server"
                else
                    // Use ls -la to get directory listing with details
                    // -F adds / to directories, -1 one entry per line
                    let command = sprintf "ls -1F %s 2>/dev/null || echo ''" remotePath
                    use cmd = client.RunCommand(command)

                    let entries =
                        cmd.Result.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.choose (fun entry ->
                            let entry = entry.Trim()
                            if String.IsNullOrWhiteSpace(entry) then None
                            elif entry.EndsWith("/") then
                                // Directory
                                let name = entry.TrimEnd('/')
                                Some {
                                    Name = name
                                    FullPath = if remotePath = "/" then "/" + name else remotePath.TrimEnd('/') + "/" + name
                                    IsDirectory = true
                                }
                            elif entry.EndsWith("@") || entry.EndsWith("*") || entry.EndsWith("|") then
                                // Skip symlinks, executables, pipes
                                None
                            else
                                // Regular file - skip for directory browsing
                                None
                        )
                        |> Array.toList

                    client.Disconnect()
                    return Result.Ok entries
            with
            | ex ->
                return Result.Error (sprintf "List directories error: %s" ex.Message)
        }

    /// Check if credentials are configured (doesn't verify they work)
    let isConfigured (host: string option) (user: string option) (keyPath: string option) : bool =
        match host, user, keyPath with
        | Some h, Some u, Some k when
            not (String.IsNullOrWhiteSpace(h)) &&
            not (String.IsNullOrWhiteSpace(u)) &&
            not (String.IsNullOrWhiteSpace(k)) -> true
        | _ -> false

    /// Create credentials from config values
    let createCredentials (host: string) (port: int) (user: string) (keyPath: string) (passphrase: string option) : SshCredentials =
        {
            Host = host
            Port = port
            Username = user
            KeyPath = keyPath
            Passphrase = passphrase
        }
