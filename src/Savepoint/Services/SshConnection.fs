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

    /// Download a file via SCP with progress callback
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

    /// List files in a remote directory matching a pattern
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
                    // Use ls command with pattern matching
                    let command = sprintf "ls -1 %s/%s 2>/dev/null || true" remotePath pattern
                    use cmd = client.RunCommand(command)

                    let files =
                        cmd.Result.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.toList

                    client.Disconnect()
                    return Result.Ok files
            with
            | ex ->
                return Result.Error (sprintf "List files error: %s" ex.Message)
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
