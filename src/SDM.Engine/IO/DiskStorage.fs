namespace SDM.Engine

open System.IO
open System
open System.Threading
open SDM.Domain

/// Thread-safe disk I/O using RandomAccess API for zero-copy
/// writes at arbitrary offsets (segment-based downloads)
module DiskStorage =
    /// Ensure directory exists, creating all intermediate directories if needed
    let ensureDirectory (path: string) =
        match Path.GetDirectoryName path with
        | null -> ()
        | dir when String.IsNullOrEmpty dir -> ()
        | dir -> Directory.CreateDirectory dir |> ignore

    /// Write a data chunk to a specific offset in the target file using
    /// RandomAccess for seeking without holding a persistent FileStream.
    let writeSegmentAsync (path: string) (offset: int64<B>) (data: ReadOnlyMemory<byte>) (ct: CancellationToken) =
        async {
            let byteOffset = int64 offset

            use handle =
                File.OpenHandle(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    FileOptions.Asynchronous
                )

            do!
                RandomAccess.WriteAsync(handle, data, byteOffset, ct).AsTask()
                |> Async.AwaitTask
        }

    /// Create an IStorageService implementation backed by disk I/O
    let create () =
        { new IStorageService with
            member _.WriteSegmentAsync(path, offset, data, ct) = writeSegmentAsync path offset data ct
            member _.EnsureDirectory path = ensureDirectory path }

/// Assembles all downloaded segments into a single contiguous output file.
/// Uses buffered sequential reads for cache-friendly I/O throughput.
module FileAssembler =
    [<Literal>]
    let private CopyBufferSize = 256 * 1024

    /// Assemble segments from temp folder into a single final file.
    /// Segments are read in offset-order and written sequentially.
    let assembleAsync (tempPath: string) (segments: Segment list) (outputPath: string) (ct: CancellationToken) =
        async {
            let sorted = segments |> List.sortBy (fun s -> s.Offset)

            use outputStream =
                new FileStream(
                    outputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    FileOptions.Asynchronous ||| FileOptions.SequentialScan
                )

            for seg in sorted do
                ct.ThrowIfCancellationRequested()

                use handle =
                    File.OpenHandle(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous)

                let segLength = int64 seg.Length
                let mutable remaining = segLength
                let mutable readOffset = int64 seg.Offset
                let buffer = Array.zeroCreate CopyBufferSize

                while remaining > 0L do
                    ct.ThrowIfCancellationRequested()

                    let toRead = int (min remaining (int64 CopyBufferSize))
                    let memory = buffer.AsMemory(0, toRead)

                    let! bytesRead =
                        RandomAccess.ReadAsync(handle, memory, readOffset, ct).AsTask()
                        |> Async.AwaitTask

                    if bytesRead = 0 then
                        remaining <- 0L
                    else
                        do!
                            outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).AsTask()
                            |> Async.AwaitTask

                        readOffset <- readOffset + int64 bytesRead
                        remaining <- remaining - int64 bytesRead

            do! outputStream.FlushAsync ct |> Async.AwaitTask
        }
