namespace StockLab.Infrastructure.AiTraderMetadata;

/// <summary>Private server-owned JSON files. Paths must be absolute; no artifact or directory discovery.</summary>
public sealed class AiTraderMetadataOptions
{
    public const string SectionName = "AiTraderMetadata";
    public string? RegistryPath { get; set; }
    public string[] BacktestReportPaths { get; set; } = [];
    public string[] BenchmarkComparisonPaths { get; set; } = [];
    public string[] PromotionEvaluationPaths { get; set; } = [];
}
