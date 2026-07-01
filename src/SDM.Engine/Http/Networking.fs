namespace SDM.Engine

open SDM.Domain
open System
open System.Net.Http.Headers
open System.Net.Http
open System.Threading
open Serilog

exception DownloadNetworkException of string * System.Net.HttpStatusCode

/// Networking Probe
type ProbeResult =
    { Size: int64<B> option
      AcceptRanges: bool
      FileName: string option
      LastModified: DateTimeOffset option
      IsRedirected: bool
      FinalUri: Uri }

module Networking =

    let private log = Log.ForContext("Source", "Networking")

    /// Helper apply atuhentication to header
    let private applyAuth (headers: HttpRequestHeaders) (auth: AuthInfo) =
        match auth with
        | Basic(u, p) ->
            let authValue =
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes $"{u}:{p}")

            headers.Authorization <- AuthenticationHeaderValue("Basic", authValue)
        | Bearer t -> headers.Authorization <- AuthenticationHeaderValue("Bearer", t)
        | NoAuth -> ()

    /// Create a Range request for a specific segment
    let createSegmentRequest (url: Uri) (offset: int64<B>) (length: int64<B>) (auth: AuthInfo) =
        let request = new HttpRequestMessage(HttpMethod.Get, url)
        let rangeEnd = int64 offset + int64 length - 1L

        request.Headers.Range <- RangeHeaderValue(int64 offset, rangeEnd)

        applyAuth request.Headers auth
        request

    /// Probes the URL to get file information with robust fallbacks and retry.
    let probeUrl (client: HttpClient) (url: Uri) (auth: AuthInfo) (ct: CancellationToken) =
        let tryProbe (method: HttpMethod) =
            async {
                use request = new HttpRequestMessage(method, url)
                applyAuth request.Headers auth

                request.Headers.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36"
                )

                let! response =
                    client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    |> Async.AwaitTask

                return response
            }

        let probeOperation (attempt: int) =
            async {
                log.Information("Probe attempt {Attempt} for {Url}", attempt + 1, url)

                let! response =
                    async {
                        let! res = tryProbe HttpMethod.Head

                        if res.IsSuccessStatusCode then
                            return res
                        else
                            return! tryProbe HttpMethod.Get
                    }

                use _res = response

                if not response.IsSuccessStatusCode then
                    return
                        raise (
                            DownloadNetworkException(
                                $"Server returned {response.StatusCode}",
                                response.StatusCode
                            )
                        )

                let size =
                    response.Content.Headers.ContentLength
                    |> Option.ofNullable
                    |> Option.map (fun s -> s * 1L<B>)

                let acceptRanges =
                    response.Headers.AcceptRanges.Contains "bytes"
                    || response.StatusCode = Net.HttpStatusCode.PartialContent

                let finalUri =
                    response.RequestMessage
                    |> Option.ofObj
                    |> Option.bind (fun r -> Option.ofObj r.RequestUri)
                    |> Option.defaultValue url

                let isRedirected = finalUri <> url

                let fileName =
                    response.Content.Headers.ContentDisposition
                    |> Option.ofObj
                    |> Option.bind (fun cd -> Option.ofObj cd.FileName)
                    |> Option.map (fun n -> n.Trim '\"')

                return
                    { Size = size
                      AcceptRanges = acceptRanges
                      FileName = fileName
                      LastModified = response.Content.Headers.LastModified |> Option.ofNullable
                      IsRedirected = isRedirected
                      FinalUri = finalUri }
            }

        async {
            try
                log.Information("Probing URL: {Url}", url)

                let config : RetryPolicy.RetryConfig =
                    { MaxRetries = 2
                      BaseDelayMs = 500
                      MaxDelayMs = 5000 }

                return! RetryPolicy.executeAsync config probeOperation ct
            with
            | :? AggregateException as ae ->
                log.Error(ae, "All probe attempts failed for {Url}", url)
                return raise (DownloadNetworkException($"Probe failed after retries: {ae.InnerExceptions.[0].Message}", Net.HttpStatusCode.ServiceUnavailable))
            | :? OperationCanceledException ->
                log.Information("Probe cancelled for {Url}", url)
                return raise (OperationCanceledException())
            | ex ->
                log.Error(ex, "Probe failed for {Url}: {Message}", url, ex.Message)
                return raise (Exception $"Networking error during probe: {ex.Message}")
        }

    /// Probes the URL without cancellation token (backward compatibility wrapper)
    let probeUrlSimple (client: HttpClient) (url: Uri) (auth: AuthInfo) =
        probeUrl client url auth CancellationToken.None
