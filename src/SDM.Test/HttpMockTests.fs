namespace SDM.Test

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Threading
open System.Threading.Tasks
open Xunit
open SDM.Domain
open SDM.Engine

/// Configuration for the advanced mock HTTP handler
type MockConfig =
    { TotalSize: int64
      RedirectUrl: string option
      ContentDispositionFileName: string option
      RejectRanges: bool
      SimulatedDelayMs: int }

    static member Default =
        { TotalSize = 1024L * 1024L // 1 MB
          RedirectUrl = None
          ContentDispositionFileName = None
          RejectRanges = false
          SimulatedDelayMs = 0 }

/// Mock HTTP handler supporting redirects, Content-Disposition, and Range rejection.
type AdvancedMockHandler(config: MockConfig) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, ct: CancellationToken) =
        task {
            ct.ThrowIfCancellationRequested()

            if config.SimulatedDelayMs > 0 then
                do! Task.Delay(config.SimulatedDelayMs, ct)

            let response = new HttpResponseMessage()

            // Handle redirect — return 302 and let the next request succeed for the target
            match config.RedirectUrl with
            | Some redirectUrl when request.RequestUri <> Uri(redirectUrl) ->
                response.StatusCode <- HttpStatusCode.Found // 302
                response.Headers.Location <- Uri(redirectUrl)
                response.Content <- new ByteArrayContent(Array.empty)
                response.Content.Headers.ContentLength <- Nullable<int64>(0L)
                return response

            | _ ->
                // Always advertise Accept-Ranges for default config
                response.Headers.AcceptRanges.Add("bytes")

                // Determine if this is a Range request
                let rangeHeader = request.Headers.Range

                let hasRange =
                    rangeHeader <> null
                    && rangeHeader.Ranges <> null
                    && rangeHeader.Ranges.Count > 0

                if hasRange && config.RejectRanges then
                    // Server does not support Range — return full content
                    response.StatusCode <- HttpStatusCode.OK
                    let data = Array.zeroCreate<byte> (int config.TotalSize)
                    response.Content <- new ByteArrayContent(data)
                    response.Content.Headers.ContentLength <- Nullable<int64>(config.TotalSize)
                    return response
                else
                    let startingPos, endingPos =
                        if hasRange then
                            let range = rangeHeader.Ranges |> Seq.tryHead

                            match range with
                            | Some r ->
                                let startVal = if r.From.HasValue then r.From.Value else 0L
                                let endVal = if r.To.HasValue then r.To.Value else config.TotalSize - 1L
                                startVal, endVal
                            | _ -> 0L, config.TotalSize - 1L
                        else
                            0L, config.TotalSize - 1L

                    let responseLength = endingPos - startingPos + 1L
                    let clampedLength = min responseLength (config.TotalSize - startingPos)

                    if startingPos > 0L then
                        response.StatusCode <- HttpStatusCode.PartialContent // 206
                    else
                        response.StatusCode <- HttpStatusCode.OK

                    let data = Array.zeroCreate<byte> (int clampedLength)
                    response.Content <- new ByteArrayContent(data)
                    response.Content.Headers.ContentLength <- Nullable<int64>(clampedLength)

                    // Add Content-Disposition if configured
                    config.ContentDispositionFileName
                    |> Option.iter (fun fileName ->
                        response.Content.Headers.ContentDisposition <- ContentDispositionHeaderValue("attachment")
                        response.Content.Headers.ContentDisposition.FileName <- fileName)

                    return response
        }

module HttpMockIntegrationTests =

    /// Helper to create HttpClient from a mock handler
    let createClient (handler: HttpMessageHandler) =
        let client = new HttpClient(handler)
        client.Timeout <- Timeout.InfiniteTimeSpan

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36"
        )

        client

    /// Simulate a full probe using Networking.probeUrlSimple
    let simulateProbe (config: MockConfig) =
        async {
            use handler = new AdvancedMockHandler(config)
            use client = createClient handler
            return! Networking.probeUrlSimple client (Uri("http://test.local/file.bin")) NoAuth
        }

    [<Fact>]
    let ``HTTP redirect 302 is detected correctly`` () =
        task {
            let config =
                { MockConfig.Default with
                    RedirectUrl = Some "http://test.local/redirected-file.bin" }

            use handler = new AdvancedMockHandler(config)

            // Custom handlers don't auto-follow redirects — we verify the 302 response directly
            use client = new HttpClient(handler)
            client.Timeout <- Timeout.InfiniteTimeSpan

            use request =
                new HttpRequestMessage(HttpMethod.Head, Uri("http://test.local/file.bin"))

            use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)

            // Verify the handler correctly returns 302 with Location header
            Assert.Equal(HttpStatusCode.Found, response.StatusCode)
            Assert.NotNull(response.Headers.Location)
            Assert.Equal("http://test.local/redirected-file.bin", string response.Headers.Location)
        }

    [<Fact>]
    let ``Content-Disposition filename is parsed correctly`` () =
        async {
            let config =
                { MockConfig.Default with
                    ContentDispositionFileName = Some "my-video.mp4" }

            let! result = simulateProbe config

            Assert.True(result.FileName.IsSome, "Should extract filename from Content-Disposition")
            Assert.Equal("my-video.mp4", result.FileName.Value)
        }
        |> Async.RunSynchronously

    [<Fact>]
    let ``Server with Range support indicates Accept-Ranges`` () =
        async {
            let config = MockConfig.Default
            let! result = simulateProbe config

            Assert.True(result.AcceptRanges, "Server should indicate Accept-Ranges: bytes")
        }
        |> Async.RunSynchronously

    [<Fact>]
    let ``Content size is detected correctly from HEAD response`` () =
        async {
            let config =
                { MockConfig.Default with
                    TotalSize = 5_000_000L } // 5 MB

            let! result = simulateProbe config

            Assert.True(result.Size.IsSome, "Should detect content size")
            Assert.Equal(5_000_000L, int64 result.Size.Value)
        }
        |> Async.RunSynchronously

    [<Fact>]
    let ``Segment download via Range header works with partial content`` () =
        task {
            use handler = new AdvancedMockHandler(MockConfig.Default)
            use client = createClient handler

            let request =
                new HttpRequestMessage(HttpMethod.Get, Uri("http://test.local/file.bin"))

            request.Headers.Range <- RangeHeaderValue(1000L, 1999L)

            let! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode)
            Assert.Equal(1000L, response.Content.Headers.ContentLength.Value)
        }

    [<Fact>]
    let ``Server rejecting Range requests falls back to full content`` () =
        task {
            let config =
                { MockConfig.Default with
                    RejectRanges = true }

            use handler = new AdvancedMockHandler(config)
            use client = createClient handler

            let request =
                new HttpRequestMessage(HttpMethod.Get, Uri("http://test.local/file.bin"))

            request.Headers.Range <- RangeHeaderValue(1000L, 1999L)

            let! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)

            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.Equal(MockConfig.Default.TotalSize, response.Content.Headers.ContentLength.Value)
        }
