namespace SDM.Engine

open System
open System.Net.Http
open System.Net
open SDM.Domain
open System.Net.Http.Headers
open System.Threading

/// Uses a shared SocketsHttpHandler for connection pooling with
/// configurable timeout and redirect policies.
module HttpClientService =
    /// Configuration for the HTTP client
    type HttpClientConfig =
        { ConnectTimeoutSeconds: int
          MaxConnectionsPerServer: int
          MaxAutomaticRedirections: int
          AllowAutoRedirect: bool
          PooledConnectionLifetimeMinutes: int }

    /// Default configuration tuned for download scenarios
    let defaultConfig =
        { ConnectTimeoutSeconds = 30
          MaxConnectionsPerServer = 16
          MaxAutomaticRedirections = 10
          AllowAutoRedirect = true
          PooledConnectionLifetimeMinutes = 5 }

    /// Create a pre-configured SocketsHttpHandler with connection pooling
    let private createHandler (config: HttpClientConfig) =
        let handler = new SocketsHttpHandler()

        handler.ConnectTimeout <- TimeSpan.FromSeconds(float config.ConnectTimeoutSeconds)
        handler.MaxConnectionsPerServer <- config.MaxConnectionsPerServer
        handler.MaxAutomaticRedirections <- config.MaxAutomaticRedirections
        handler.AllowAutoRedirect <- config.AllowAutoRedirect
        handler.PooledConnectionLifetime <- TimeSpan.FromMinutes(float config.PooledConnectionLifetimeMinutes)
        handler.PooledConnectionIdleTimeout <- TimeSpan.FromMinutes 2.0
        handler.AutomaticDecompression <- DecompressionMethods.All
        handler

    /// Apply with headers to a request
    let private applyAuth (headers: HttpRequestHeaders) (auth: AuthInfo) =
        match auth with
        | Basic(u, p) ->
            let encoded = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes $"{u}:{p}")

            headers.Authorization <- AuthenticationHeaderValue("Basic", encoded)

        | Bearer t -> headers.Authorization <- AuthenticationHeaderValue("Bearer", t)
        | NoAuth -> ()

    let userAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36"

    /// Create a shared SocketsHttpHandler for the entire application lifetime.
    /// All per-download HttpServices share this handler for connection pooling.
    let createSharedHandler (config: HttpClientConfig) = createHandler config

    /// Internal helper: build a per-download HttpClient backed by the shared handler.
    /// Per-download auth, headers, and cookies are applied per-request (not on the shared handler).
    let private createClient (handler: SocketsHttpHandler) =
        let client = new HttpClient(handler, disposeHandler = false)
        client.Timeout <- Timeout.InfiniteTimeSpan
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent)
        client

    /// Create an IHttpService with its own handler (for standalone usage, e.g. tests).
    let create (config: HttpClientConfig) (auth: AuthInfo) (headers: Map<string, string>) (cookies: string option) =
        let handler = createHandler config
        let client = createClient handler

        headers
        |> Map.iter (fun k v -> client.DefaultRequestHeaders.TryAddWithoutValidation(k, v) |> ignore)

        cookies
        |> Option.iter (fun c -> client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", c) |> ignore)

        { new IHttpService with
            member _.GetStreamAsync(url, rangeStart, rangeEnd, ct) =
                async {
                    let request = new HttpRequestMessage(HttpMethod.Get, url)
                    applyAuth request.Headers auth

                    let endByte =
                        rangeEnd
                        |> Option.map (fun e -> Nullable<int64>(int64 e))
                        |> Option.defaultValue (Nullable())

                    request.Headers.Range <- RangeHeaderValue(int64 rangeStart, endByte)

                    let! response =
                        client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        |> Async.AwaitTask

                    return response
                } }

    /// Create an IHttpService backed by a shared SocketsHttpHandler.
    /// Per-download headers/cookies are applied per-request to avoid contaminating the shared handler.
    let createWithHandler
        (handler: SocketsHttpHandler)
        (auth: AuthInfo)
        (headers: Map<string, string>)
        (cookies: string option)
        =
        let client = createClient handler

        headers
        |> Map.iter (fun k v -> client.DefaultRequestHeaders.TryAddWithoutValidation(k, v) |> ignore)

        cookies
        |> Option.iter (fun c -> client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", c) |> ignore)

        { new IHttpService with
            member _.GetStreamAsync(url, rangeStart, rangeEnd, ct) =
                async {
                    let request = new HttpRequestMessage(HttpMethod.Get, url)
                    applyAuth request.Headers auth

                    let endByte =
                        rangeEnd
                        |> Option.map (fun e -> Nullable<int64>(int64 e))
                        |> Option.defaultValue (Nullable())

                    request.Headers.Range <- RangeHeaderValue(int64 rangeStart, endByte)

                    let! response =
                        client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        |> Async.AwaitTask

                    return response
                } }
