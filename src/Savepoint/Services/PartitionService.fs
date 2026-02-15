namespace Savepoint.Services

open System
open System.Management

/// Service for enumerating disk partitions using WMI
module PartitionService =

    /// Information about a disk partition
    type PartitionInfo = {
        DevicePath: string       // e.g., "\Device\Harddisk1\Partition1"
        DiskNumber: int
        PartitionNumber: int
        SizeGB: float
        DisplayName: string      // "Disk 1 Partition 1 (500 GB)"
    }

    /// Query disk partitions using WMI Win32_DiskPartition
    let getPartitions () : PartitionInfo list =
        try
            use searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskPartition")
            use results = searcher.Get()

            results
            |> Seq.cast<ManagementObject>
            |> Seq.map (fun partition ->
                let diskIndex = partition.["DiskIndex"] :?> uint32 |> int
                let partitionIndex = partition.["Index"] :?> uint32 |> int
                let sizeBytes = partition.["Size"] :?> uint64
                let sizeGB = float sizeBytes / (1024.0 * 1024.0 * 1024.0)

                // VeraCrypt expects device paths in format: \Device\Harddisk{N}\Partition{M}
                // Note: VeraCrypt uses 1-based partition numbering
                let devicePath = sprintf @"\Device\Harddisk%d\Partition%d" diskIndex (partitionIndex + 1)

                {
                    DevicePath = devicePath
                    DiskNumber = diskIndex
                    PartitionNumber = partitionIndex + 1
                    SizeGB = Math.Round(sizeGB, 1)
                    DisplayName = sprintf "Disk %d Partition %d (%.1f GB)" diskIndex (partitionIndex + 1) sizeGB
                }
            )
            |> Seq.sortBy (fun p -> (p.DiskNumber, p.PartitionNumber))
            |> Seq.toList
        with ex ->
            // Return empty list on error (e.g., WMI not available)
            []

    /// Get partitions asynchronously
    let getPartitionsAsync () : Async<PartitionInfo list> =
        async {
            return getPartitions ()
        }
