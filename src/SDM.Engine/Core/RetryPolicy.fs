namespace SDM.Engine

open System
open System.Threading

/// Exponential backoff retry policy with jitter for resilient downloads.
/// Prevents thundering-herd on transient failures.
module RetryPolicy =
    type RetryConfig =
        { MaxRetries: int
          BaseDelayMs: int
          MaxDelayMs: int }

    let defaults =
        { MaxRetries = 5
          BaseDelayMs = 1000
          MaxDelayMs = 30000 }

    /// Calculate delay with exponential backoff + jitter
    let private calculateDelay (config: RetryConfig) (attempt: int) =
        let baseDelay = float config.BaseDelayMs * Math.Pow(2.0, float attempt)
        let capped = min baseDelay (float config.MaxDelayMs)
        let jitter = capped * 0.25 * (Random.Shared.NextDouble() * 2.0 - 1.0)
        int (capped + jitter) |> max 100

    /// Execute operation with retry logic
    let executeAsync (config: RetryConfig) (operation: int -> Async<'T>) (ct: CancellationToken) =
        let rec attempt (n: int) (lastError: exn option) =
            async {
                if n > config.MaxRetries then
                    return
                        raise (
                            AggregateException(
                                $"Operation failed after {config.MaxRetries} retries",
                                lastError |> Option.toList
                            )
                        )

                ct.ThrowIfCancellationRequested()

                try
                    return! operation n
                with
                | :? OperationCanceledException -> return raise (OperationCanceledException())
                | ex ->
                    let delay = calculateDelay config n

                    do! Async.Sleep delay

                    return! attempt (n + 1) (Some ex)
            }

        attempt 0 None
