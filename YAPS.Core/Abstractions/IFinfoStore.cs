using Yaps.Core.Models;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Abstraction over .finfo persistence. The default FileFinfoStore
/// delegates to the static FinfoData helpers; introducing the seam
/// lets callers be substituted in tests (in-memory store) or in
/// future (e.g. SQLite-backed store) without changing the consumers.
/// </summary>
public interface IFinfoStore
{
    FinfoData? Read(string path);
    void Write(string path, FinfoData data);
}

public sealed class FileFinfoStore : IFinfoStore
{
    public FinfoData? Read(string path) => FinfoData.ReadFromFile(path);
    public void Write(string path, FinfoData data) => FinfoData.WriteToFile(path, data);
}
