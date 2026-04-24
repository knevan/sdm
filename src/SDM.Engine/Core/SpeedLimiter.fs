namespace SDM.Engine

open System
open System.Threading

/// Token-bucket based speed limiter for bandwidth throttling.
/// Lock-free using Interlocked operations for minimal contention
module SpeedLimiter =
    type ThrottleState =
        {
            /// Maximum bytes per second ( 0 = unlimited)
            LimitBps: int64
            /// Tokens currently available in the bucket
            mutable AvailableTokens: int64
            /// Last time tokens were refilled
            mutable LastRefillTick: int64
            /// Signal to wake sleeping threads on limit change
            WakeSignal: ManualResetEventSlim
        }

    /// Create a new throttle state with the given speed limit
    let create (limitKBps: int) =
        { LimitBps = int64 limitKBps * 1024L
          AvailableTokens = int64 limitKBps * 1024L
          LastRefillTick = Environment.TickCount64
          WakeSignal = new ManualResetEventSlim false }

    /// Update the speed limit dynamically
    let updateLimit (state: ThrottleState) (limitKbps: int) =
        state.WakeSignal.Set()

        { state with
            LimitBps = int64 limitKbps * 1024L }

    /// Consume tokens for the given byte count, blocking if
    /// Returns immediately if no speed limit is set.
    let consumeAsync (state: ThrottleState) (byteCount: int) (ct: CancellationToken) =
        async {
            if state.LimitBps <= 0L then
                () // No throttling
            else
                let now = Environment.TickCount64
                let elapsed = now - Interlocked.Exchange(&state.LastRefillTick, now)

                let newTokens = state.LimitBps * elapsed / 1000L
                let _ = Interlocked.Add(&state.AvailableTokens, newTokens) |> min state.LimitBps // Cap at burst size

                let remaining = Interlocked.Add(&state.AvailableTokens, int64 -byteCount)

                // If we've exceeded the budget, sleep proportionally
                if remaining < 0L then
                    let sleepMs = int (-remaining * 1000L / state.LimitBps) |> max 1
                    state.WakeSignal.Reset()

                    use! cancelReg =
                        async {
                            let reg = ct.Register(fun () -> state.WakeSignal.Set())
                            return reg
                        }

                    do! Async.AwaitWaitHandle(state.WakeSignal.WaitHandle, sleepMs) |> Async.Ignore

                    ct.ThrowIfCancellationRequested()
        }
