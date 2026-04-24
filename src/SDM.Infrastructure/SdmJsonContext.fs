namespace SDM.Infrastructure

open System.Text.Json.Serialization
open System.Text.Json
open SDM.Domain

[<JsonSourceGenerationOptions(WriteIndented = true)>]
[<JsonSerializable(typeof<AppConfig>)>]
[<JsonSerializable(typeof<ProxyConfig>)>]
[<JsonSerializable(typeof<ProxyType>)>]
[<JsonSerializable(typeof<FileConflictMode>)>]
[<JsonSerializable(typeof<DownloadEntry>)>]
[<JsonSerializable(typeof<DownloadStatus>)>]
[<JsonSerializable(typeof<Segment list>)>]
[<JsonSerializable(typeof<System.Collections.Generic.Dictionary<string, string>>)>]
[<JsonSerializable(typeof<AuthInfo>)>]
type SdmJsonContext(options: JsonSerializerOptions) =
    inherit JsonSerializerContext(options)
