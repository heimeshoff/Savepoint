namespace Savepoint.Services

open System
open System.IO
open Renci.SshNet

/// SSH/SCP connection service for Linux server backups
module SshConnection =

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
        let authMethod =
            match creds.Passphrase with
            | Some passphrase ->
                new PrivateKeyAuthenticationMethod(
                    creds.Username,
                    new PrivateKeyFile(creds.KeyPath, passphrase)
                ) :> AuthenticationMethod
            | None ->
                new PrivateKeyAuthenticationMethod(
                    creds.Username,
                    new PrivateKeyFile(creds.KeyPath)
                ) :> AuthenticationMethod
        ConnectionInfo(creds.Host, creds.Port, creds.Username, authMethod)

    /// Test if the server is reachable (for status indicator)
    let testConnection (creds: SshCredentials) : Async<ConnectionStatus> =
        async {
            try
                // Validate key file exists first
                if not (File.Exists(creds.KeyPath)) then
                    return Error (sprintf "SSH key file not found: %s" creds.KeyPath)
                else
                    let connectionInfo = createConnectionInfo creds
                    use client = new SshClient(connectionInfo)
                    client.ConnectionInfo.Timeout <- TimeSpan.FromSeconds(10.0)
                    client.Connect()
                    let result = if client.IsConnected then Connected else Disconnected
                    client.Disconnect()
                    return result
            with
            | :? Renci.SshNet.Common.SshAuthenticationException as ex ->
                return Error (sprintf "Authentication failed: %s" ex.Message)
            | :? Renci.SshNet.Common.SshConnectionException as ex ->
                return Error (sprintf "Connection failed: %s" ex.Message)
            | :? System.Net.Sockets.SocketException as ex ->
                return Error (sprintf "Network error: %s" ex.Message)
            | ex ->
                return Error (sprintf "SSH error: %s" ex.Message)
        }

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
