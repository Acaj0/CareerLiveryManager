using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.App.ViewModels;

/// <summary>A <see cref="SetupIssue"/> prepared for display in the Setup screen's problem list.</summary>
public sealed class SetupIssueItem
{
    public SetupIssueItem(SetupIssue issue)
    {
        Issue = issue;
    }

    public SetupIssue Issue { get; }

    public string Id => Issue.Id;
    public string Message => Issue.Message;

    public string SeverityLabel => Issue.Severity switch
    {
        SetupIssueSeverity.Error => "PROBLEM",
        SetupIssueSeverity.Warning => "WARNING",
        _ => "NOTE",
    };

    /// <summary>Same palette the Setup screen already uses for its status line.</summary>
    public string Color => Issue.Severity switch
    {
        SetupIssueSeverity.Error => "#D95C5C",
        SetupIssueSeverity.Warning => "#D9A03C",
        _ => "#8B95A5",
    };

    public bool HasFix => Issue.SuggestedFix is not null;
    public string FixLabel => Issue.SuggestedFix?.Description ?? string.Empty;
}
