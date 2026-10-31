namespace PcTool.Core.Models;

public enum FindingSeverity { Info, Warning, Problem }

public sealed record DiagnosticFinding(
    string Category,
    FindingSeverity Severity,
    string Title,
    string Detail,
    string? SuggestedFix = null);
