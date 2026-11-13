using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockLab.Application.DTOs.AiTrader;
using static StockLab.Infrastructure.AiTraderMetadata.MetadataJson;

namespace StockLab.Infrastructure.AiTraderMetadata;

/// <summary>Closed metadata projection only; never authenticates or loads an estimator or its sources.</summary>
internal static class BoostedSnapshotCatalog
{
    private const string TargetDefinition = "open[t+21]/open[t+1]-1 > .002";
    private static readonly string[] Features = [
        "return_1d", "rsi_14", "momentum_10", "volatility_20", "close_ma5", "close_ma20", "ma_ratio", "relative_volume",
        "monthly_range_9", "monthly_range_12", "monthly_range_19", "monthly_rsi", "monthly_multiplier",
        "momentum_20", "momentum_63", "momentum_126", "momentum_252", "close_ma50", "close_ma200", "volatility_63",
        "drawdown_252", "relative_momentum_20", "relative_momentum_63", "relative_momentum_252",
        "vix_level", "vix_change5", "vix_term_ratio", "vix_term_change5"];
    private const string Parameters = """
        {"categorical_features":"from_dtype","class_weight":null,"early_stopping":false,"interaction_cst":null,
        "l2_regularization":1,"learning_rate":0.05,"loss":"log_loss","max_bins":255,"max_depth":null,
        "max_features":1,"max_iter":200,"max_leaf_nodes":15,"min_samples_leaf":20,"monotonic_cst":null,
        "n_iter_no_change":10,"random_state":42,"scoring":"loss","tol":1e-7,"validation_fraction":0.1,
        "verbose":0,"warm_start":false}
        """;
    private const string Evidence = """
        {"recipe":"frozen_period_parity",
        "historical":"known-date purge on supplied current-vintage frame; past publication not certified",
        "prospective":{"status":"PENDING","metrics":null}}
        """;

    internal static (AiModelPeriod Training, AiModelForecast Forecast) Parse(
        JsonElement record, DateTimeOffset trained, string status)
    {
        Require(Name(record) == "hgb_swing20_market28" && status == "Archived"
            && record.GetProperty("metrics").ValueKind == JsonValueKind.Null
            && record.GetProperty("holdout_sha256").ValueKind == JsonValueKind.Null);
        var report = record.GetProperty("training_report");
        Keys(report, "snapshot_metadata estimator_sha256");
        Hash(report, "estimator_sha256");
        var metadata = report.GetProperty("snapshot_metadata");
        Keys(metadata, "schema_version model_name model_type feature_columns target_definition known_date_offset_sessions promotion_authorized "
            + "fit_audit refit_frequency period_key last_prediction frame_sha256 reference_predictions_sha256 fit_rows_sha256 "
            + "prediction_features_sha256 sources_sha256 parameters environment trained_at_utc omp_num_threads evidence");
        Require(Count(metadata, "schema_version") == 1 && Name(metadata) == "hgb_swing20_market28"
            && Text(metadata, "model_type") == "hist_gradient_boosting"
            && metadata.GetProperty("feature_columns").ValueKind == JsonValueKind.Array
            && metadata.GetProperty("feature_columns").EnumerateArray().Select(Text).SequenceEqual(Features)
            && Text(metadata, "target_definition") == TargetDefinition
            && Count(metadata, "known_date_offset_sessions") == 21 && !Boolean(metadata, "promotion_authorized")
            && Text(metadata, "omp_num_threads") == "1"
            && Timestamp(metadata, "trained_at_utc") == trained && trained <= DateTimeOffset.UtcNow);
        using var parameters = JsonDocument.Parse(Parameters);
        using var evidence = JsonDocument.Parse(Evidence);
        Require(JsonElement.DeepEquals(metadata.GetProperty("parameters"), parameters.RootElement)
            && JsonElement.DeepEquals(metadata.GetProperty("evidence"), evidence.RootElement));
        foreach (var key in new[] { "frame_sha256", "reference_predictions_sha256", "fit_rows_sha256", "prediction_features_sha256" })
            Hash(metadata, key);
        var sources = metadata.GetProperty("sources_sha256");
        Require(sources.ValueKind == JsonValueKind.Object && sources.EnumerateObject().Any());
        foreach (var source in sources.EnumerateObject())
        {
            Require(source.Name.Length is > 0 and <= 4096);
            Hash(sources, source.Name);
        }
        var environment = metadata.GetProperty("environment");
        Keys(environment, "python scikit_learn numpy scipy joblib");
        foreach (var item in environment.EnumerateObject()) Require(Text(item.Value).Length is > 0 and <= 64);
        Require(Regex.IsMatch(Text(environment, "python"), @"^\d+\.\d+\.\d+\z", RegexOptions.CultureInvariant));

        var fit = metadata.GetProperty("fit_audit");
        Keys(fit, "recipe label first_fit last_fit last_target first_prediction train_rows training_positive_rate date_weighted "
            + "training_unweighted_positive_rate training_weighted_positive_rate training_dates training_identities training_rows");
        var recipe = Text(fit, "recipe");
        Require(recipe.StartsWith("boosted_", StringComparison.Ordinal) && recipe.Length <= 128
            && Text(fit, "label") == "target_20" && Boolean(fit, "date_weighted"));
        var rows = Count(fit, "train_rows", true);
        var dates = Count(fit, "training_dates", true);
        Require(rows == Count(fit, "training_rows", true) && Count(fit, "training_identities", true) == 5
            && rows >= 5 && dates <= rows && rows <= (long)dates * 5);
        foreach (var key in new[] { "training_positive_rate", "training_unweighted_positive_rate", "training_weighted_positive_rate" })
            Require(Rate(fit, key) is > 0 and < 1);
        Require(Rate(fit, "training_positive_rate") == Rate(fit, "training_weighted_positive_rate"));
        var start = Day(fit, "first_fit");
        var end = Day(fit, "last_fit");
        var known = Day(fit, "last_target");
        var first = Day(fit, "first_prediction");
        var last = Day(metadata, "last_prediction");
        var cadence = Text(metadata, "refit_frequency");
        Require(cadence is "annual" or "monthly" && first.Year > 4
            && first.AddYears(-4) <= start && start <= end && end < known && known < first && first <= last
            && DateOnly.FromDateTime(trained.UtcDateTime) >= known);
        var pattern = cadence == "annual" ? "yyyy" : "yyyy-MM";
        Require(Text(metadata, "period_key") == first.ToString(pattern, CultureInfo.InvariantCulture)
            && first.ToString(pattern, CultureInfo.InvariantCulture) == last.ToString(pattern, CultureInfo.InvariantCulture));
        return (new(start, end), new("target_20", TargetDefinition, 20, Features.ToArray(), cadence, first, "PENDING"));
    }
}
