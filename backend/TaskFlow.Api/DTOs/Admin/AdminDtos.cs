namespace TaskFlow.Api.DTOs.Admin;

public class AdminOverviewResponse
{
    public IReadOnlyCollection<AdminMetricResponse> Metrics { get; set; } = [];
    public IReadOnlyCollection<AdminSecurityEvidenceResponse> SecurityEvidence { get; set; } = [];
}

public class AdminMetricResponse
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public string HelpText { get; set; } = string.Empty;
}

public class AdminSecurityEvidenceResponse
{
    public string Area { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}
