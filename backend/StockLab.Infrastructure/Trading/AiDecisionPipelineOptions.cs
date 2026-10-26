namespace StockLab.Infrastructure.Trading;

/// <summary>Operator-owned local inputs. Unconfigured pipelines never prevent normal HTTP startup.</summary>
public sealed class AiDecisionPipelineOptions
{
    public const string SectionName = "AiDecisionPipeline";
    public string? PythonExecutable { get; set; }
    public string? MlDirectory { get; set; }
    public string? DatasetPath { get; set; }
    public string? ArtifactsDirectory { get; set; }
    public string? ModelName { get; set; }
    public string[] Symbols { get; set; } = [];
    public bool TrustedArtifacts { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}
