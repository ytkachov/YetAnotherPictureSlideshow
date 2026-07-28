using System;
using System.Text.Json.Serialization;

namespace Yaps.Core.Models.Stats;

/// <summary>
/// One photo's entry in the show registry. Mutable + settable properties
/// because this is both the in-memory counter and the persisted JSON shape.
/// The full path is the identity — a moved / renamed photo starts a new
/// history, which is the honest answer given we have no stable per-file id.
/// </summary>
public sealed class PhotoStatEntry
{
    public string Path { get; set; } = string.Empty;

    /// <summary>How many times the slideshow picked this photo for display.</summary>
    public int ShowCount { get; set; }

    public DateTime? FirstShownUtc { get; set; }
    public DateTime? LastShownUtc { get; set; }

    /// <summary>How many times loading / decoding the photo threw.</summary>
    public int FailureCount { get; set; }

    public DateTime? LastFailureUtc { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// True when the last library scan found this file. Runtime-only: it is
    /// re-derived on every start from the scan, so persisting it would just
    /// let a stale value contradict reality.
    /// </summary>
    [JsonIgnore]
    public bool InLibrary { get; set; }

    public PhotoStatEntry Copy() => new()
    {
        Path = Path,
        ShowCount = ShowCount,
        FirstShownUtc = FirstShownUtc,
        LastShownUtc = LastShownUtc,
        FailureCount = FailureCount,
        LastFailureUtc = LastFailureUtc,
        LastError = LastError,
        InLibrary = InLibrary,
    };
}
