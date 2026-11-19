using System.Text.Json.Serialization;

namespace StockLab.Application.DTOs.AiTrader;

public sealed record BoostedDiagnosticDecision(string Symbol, double ProbabilityUp, string Action, string Reason,
    double CloseMa50, double CloseMa200, [property: JsonPropertyName("momentum_20")] double Momentum20);

/// <summary>Raw Archived research diagnostic, deliberately unrelated to persistable inference batches.</summary>
public sealed record BoostedDiagnosticBatch(string ModelVersion, DateOnly FeatureDate, DateTimeOffset AsOfUtc,
    DateTimeOffset PreparedAtUtc, DateTimeOffset InferredAtUtc, DateTimeOffset TrainedAtUtc, DateOnly FitBoundary,
    bool ModelCreatedAfterAsOf, string InputManifestSha256, string FeatureFrameSha256, string PolicyContextSha256,
    string ArtifactSha256, string RegistrySha256, IReadOnlyList<BoostedDiagnosticDecision> Decisions)
{
    public int SchemaVersion => 1;
    public string Mode => "diagnostic_only";
    public bool ExecutionEligible => false;
    public string ModelName => "hgb_swing20_market28";
    public string ModelStatus => "Archived";
    public string TargetName => "target_20";
    public string TargetDefinition => "open[t+21]/open[t+1]-1 > .002";
    public int HorizonSessions => 20;
    public string Policy => "trend200-immediate-v1";
    public string ProspectiveStatus => "PENDING";
}
