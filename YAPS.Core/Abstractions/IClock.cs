using System;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Thin wrapper over DateTime so consumers don't depend on the static
/// system clock. Lets the screensaver's night-time logic, the
/// statistics rollover ("every day at 20:00") and any future time-based
/// behaviour be driven from configuration or a fake in tests.
/// </summary>
public interface IClock
{
    DateTime Now { get; }
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
