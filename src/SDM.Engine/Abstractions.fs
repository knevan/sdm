namespace SDM.Engine

open System
open System.Net.Http
open SDM.Domain

/// interface for Http client
type IHttpService =
    abstract member GetStreamAsync:
        url: Uri * rangeStart: int64<B> * rangeEnd: int64<B> option * ct: Threading.CancellationToken ->
            Async<HttpResponseMessage>

/// Interface for Disk operations
type IStorageService =
    abstract member WriteSegmentAsync:
        path: string * offset: int64<B> * data: ReadOnlyMemory<byte> * ct: Threading.CancellationToken -> Async<unit>

    abstract member EnsureDirectory: path: string -> unit
