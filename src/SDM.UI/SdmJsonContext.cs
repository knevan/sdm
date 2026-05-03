using System.Text.Json.Serialization;
using SDM.Domain;
using SDM.Infrastructure;

namespace SDM.UI;

/// <summary>
/// Serialization Context. 
/// Source-generated JSON context placed in the C# UI Layer.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Domain Types
[JsonSerializable(typeof(DownloadEntry))]
[JsonSerializable(typeof(DownloadStatus))]
[JsonSerializable(typeof(Segment))]
[JsonSerializable(typeof(SegmentStatus))]
[JsonSerializable(typeof(AuthInfo))]
[JsonSerializable(typeof(HashAlgorithm))]
[JsonSerializable(typeof(Microsoft.FSharp.Collections.FSharpList<Segment>))]
[JsonSerializable(typeof(Microsoft.FSharp.Collections.FSharpMap<string, string>))]
[JsonSerializable(typeof(Microsoft.FSharp.Core.FSharpOption<Tuple<HashAlgorithm, string>>))]
// Infrastructure Types
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ProxyConfig))]
[JsonSerializable(typeof(ProxyType))]
[JsonSerializable(typeof(FileConflictMode))]
public partial class SdmJsonContext : JsonSerializerContext
{
}