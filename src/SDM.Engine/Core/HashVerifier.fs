namespace SDM.Engine

open System
open System.Security.Cryptography
open System.Threading
open System.IO
open SDM.Domain

/// File integrity verification using streaming hash computation.
/// Processes file in chunks to keep memory usage constant
/// regardless of file size.
module HashVerifier =
    [<Literal>]
    let private BufferSize = 128 * 1024

    /// Create a HashAlgorithm instance from our domain type
    let private createAlgorithm (algo: HashAlgorithm) : Security.Cryptography.HashAlgorithm =
        match algo with
        | MD5 -> MD5.Create()
        | SHA1 -> SHA1.Create()
        | SHA256 -> SHA256.Create()
        | SHA512 -> SHA512.Create()

    /// Compute hash of a file as a lowercase hex string
    /// Streams the file in chunks to avoid loading large files into memory
    let computeHash (algo: HashAlgorithm) (filePath: string) (ct: CancellationToken) =
        async {
            use hashImpl = createAlgorithm algo

            use stream =
                new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true)

            let buffer = Array.zeroCreate BufferSize
            let mutable continueReading = true

            while continueReading do
                ct.ThrowIfCancellationRequested()

                let! bytesRead = stream.ReadAsync(buffer.AsMemory(), ct).AsTask() |> Async.AwaitTask

                if bytesRead = 0 then
                    hashImpl.TransformFinalBlock(Array.empty, 0, 0) |> ignore
                    continueReading <- false
                else
                    hashImpl.TransformBlock(buffer, 0, bytesRead, null, 0) |> ignore

            match hashImpl.Hash with
            | null -> return failwith "Hash computation failed: internal state returned null hash."
            | bytes -> return Convert.ToHexString(bytes).ToLowerInvariant()
        }

    /// Verify file againts an expected hash
    let verifyHash (algo: HashAlgorithm) (expectedHash: string) (filePath: string) (ct: CancellationToken) =
        async {
            let! computed = computeHash algo filePath ct
            return String.Equals(computed, expectedHash, StringComparison.OrdinalIgnoreCase)
        }
