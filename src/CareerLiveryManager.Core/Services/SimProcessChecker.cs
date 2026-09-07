using System.Diagnostics;

namespace CareerLiveryManager.Core.Services;

public sealed class SimProcessChecker
{
    public bool IsSimRunning() => Process.GetProcessesByName("FlightSimulator2024").Length > 0;
}
