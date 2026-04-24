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

    /// Create an IHttpService backed by a shared HttpClient instance.
    /// The client is designed to be long-lived (one per application lifetime).
    let create (config: HttpClientConfig) (auth: AuthInfo) (headers: Map<string, string>) (cookies: string option) =
        let handler = createHandler config
        let client = new HttpClient(handler)
        // Manage timeouts per-request via CancellationToken
        client.Timeout <- Timeout.InfiniteTimeSpan

        headers
        |> Map.iter (fun k v -> client.DefaultRequestHeaders.TryAddWithoutValidation(k, v) |> ignore)

        cookies
        |> Option.iter (fun c -> client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", c) |> ignore)

        { new IHttpService with
            member _.GetStreamAsync(url, rangeStart, rangeEnd, ct) =
                async {
                    let request = new HttpRequestMessage(HttpMethod.Get, url)
                    applyAuth request.Headers auth

                    let startbyte = int64 rangeStart

                    let endByte =
                        rangeEnd
                        |> Option.map (fun e -> Nullable<int64>(int64 e))
                        |> Option.defaultValue (Nullable())

                    request.Headers.Range <- RangeHeaderValue(startbyte, endByte)

                    let! response =
                        client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        |> Async.AwaitTask

                    return response
                } }
