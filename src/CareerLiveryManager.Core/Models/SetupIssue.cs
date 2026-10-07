namespace CareerLiveryManager.Core.Models;

public enum SetupIssueSeverity
{
    /// <summary>Explains an unexpected-looking result; never blocks anything.</summary>
    Info,

    /// <summary>Probably a mistake. The user may continue after confirming.</summary>
    Warning,

    /// <summary>The configuration can't work. Saving is blocked until it's fixed.</summary>
    Error,
}

/// <summary>
/// A one-click correction a <see cref="SetupIssue"/> can offer: replacement folder paths. A null
/// path means "leave that folder as the user chose it".
/// </summary>
public sealed record SetupFix(string Description, string? OfficialPath, string? CommunityPath);

/// <summary>One finding of <see cref="Services.SetupValidator"/>. <see cref="Id"/> is stable ("S01"...)
/// so it can be referenced in logs, bug reports and tests.</summary>
public sealed record SetupIssue(string Id, SetupIssueSeverity Severity, string Message, SetupFix? SuggestedFix = null);
