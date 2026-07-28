using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Yaps.Core.Models.Stats;

namespace Yaps.Infrastructure.Statistics;

/// <summary>
/// On-disk shape of the show registry. Only entries that carry information
/// (at least one show or one failure) are written — the "never shown" set is
/// re-derived from the library scan on every start, so persisting it would
/// just double the file for no gain.
/// </summary>
internal sealed class PhotoStatsFile
{
    public int SchemaVersion { get; set; }
    public DateTime SavedUtc { get; set; }
    public List<PhotoStatEntry> Entries { get; set; } = [];
}

// Source-generated serialisation: no reflection at runtime, and the shape is
// checked at build time. WhenWritingNull keeps the (large) file free of the
// null timestamps most entries carry.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PhotoStatsFile))]
internal sealed partial class PhotoStatsJsonContext : JsonSerializerContext
{
}
