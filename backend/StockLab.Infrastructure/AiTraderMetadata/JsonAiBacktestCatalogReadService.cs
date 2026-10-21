using System.Text.Json;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using static StockLab.Infrastructure.AiTraderMetadata.MetadataJson;

namespace StockLab.Infrastructure.AiTraderMetadata;

public sealed class JsonAiBacktestCatalogReadService(IOptions<AiTraderMetadataOptions> options) : IAiBacktestCatalogReadService
{
    public async Task<IReadOnlyList<AiBacktestCatalogItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        var sources = (settings.BacktestReportPaths ?? []).Select(path => (Path: path, Kind: "backtesting"))
            .Concat((settings.BenchmarkComparisonPaths ?? []).Select(path => (Path: path, Kind: "benchmark_comparison")))
            .Concat((settings.PromotionEvaluationPaths ?? []).Select(path => (Path: path, Kind: "model_promotion"))).ToArray();
        if (sources.Length == 0) throw new CatalogUnavailableException("backtest");
        if (sources.Length > 64 || sources.Select(source => source.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length)
            throw new CatalogUnavailableException("backtest", true);
        var items = new List<AiBacktestCatalogItem>();
        foreach (var source in sources)
        {
            using var document = await ReadAsync(source.Path, "backtest", cancellationToken);
            try
            {
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array) { Require(root.GetArrayLength() == 0); continue; }
                switch (source.Kind)
                {
                    case "backtesting": items.Add(ParseReport(root)); break;
                    case "benchmark_comparison":
                        Keys(root, "benchmark comparison");
                        items.Add(ParseComparison(root.GetProperty("benchmark"), root.GetProperty("comparison")));
                        break;
                    case "model_promotion": items.AddRange(PromotionMetadata.Parse(root)); break;
                }
            }
            catch (Exception error) when (error is FormatException or KeyNotFoundException or InvalidOperationException or OverflowException)
            { throw new CatalogUnavailableException("backtest", true); }
        }
        return items;
    }

    internal static AiBacktestCatalogItem ParseReport(JsonElement report)
    {
        Keys(report, "model_name initial_capital final_cash final_positions_value final_value total_return win_rate max_drawdown decision_count buy_decisions sell_decisions hold_decisions executed_trades executed_buys executed_sells winning_closed_trades losing_or_flat_closed_trades open_positions_final start_date end_date symbols config buy_threshold sell_threshold skipped_low_confidence skipped_no_position skipped_max_positions skipped_exposure skipped_buying_power no_next_session execution_timing commission slippage");
        var name = Name(report);
        var start = Day(report, "start_date");
        var end = Day(report, "end_date");
        var symbols = Symbols(report);
        var capital = Number(report, "initial_capital");
        var final = Number(report, "final_value");
        var totalReturn = Number(report, "total_return");
        ValidateValue(report, capital, final, totalReturn);
        Require(start <= end && Text(report, "execution_timing") == "decision after close[t]; next observed session open for the same symbol"
            && Number(report, "commission") == 0 && Number(report, "slippage") == 0);
        var config = report.GetProperty("config");
        ValidateConfig(config);
        Require(Number(config, "initial_capital") == capital && Rate(report, "sell_threshold") < Rate(report, "buy_threshold"));
        var decisions = Count(report, "decision_count");
        var buyDecisions = Count(report, "buy_decisions");
        var sellDecisions = Count(report, "sell_decisions");
        var holds = Count(report, "hold_decisions");
        var trades = Count(report, "executed_trades");
        var buys = Count(report, "executed_buys");
        var sells = Count(report, "executed_sells");
        var wins = Count(report, "winning_closed_trades");
        var losses = Count(report, "losing_or_flat_closed_trades");
        var open = Count(report, "open_positions_final");
        var low = Count(report, "skipped_low_confidence");
        var noPosition = Count(report, "skipped_no_position");
        var maxPositions = Count(report, "skipped_max_positions");
        var exposure = Count(report, "skipped_exposure");
        var buyingPower = Count(report, "skipped_buying_power");
        var noNext = Count(report, "no_next_session");
        Require(decisions == (long)buyDecisions + sellDecisions + holds && trades == (long)buys + sells && sells == (long)wins + losses
            && decisions == (long)trades + holds + low + noPosition + maxPositions + exposure + buyingPower + noNext
            && buys <= buyDecisions && sells <= sellDecisions && noPosition <= sellDecisions - sells
            && (long)maxPositions + exposure + buyingPower <= buyDecisions - buys
            && open <= symbols.Length && open <= Count(config, "max_open_positions", true) && open <= buys
            && (open == 0) == (Number(report, "final_positions_value") == 0) && (sells == 0 || buys > 0));
        if (trades == 0)
            Require(final == capital && Number(report, "final_cash") == capital && Rate(report, "max_drawdown") == 0);
        var winRate = Rate(report, "win_rate");
        Close(winRate, sells == 0 ? 0 : (decimal)wins / sells);
        return new(name, null, start, end, capital, final, totalReturn, winRate, Rate(report, "max_drawdown"), trades,
            null, null, null, "backtesting", null);
    }

    internal static void ValidateConfig(JsonElement config)
    {
        Keys(config, "initial_capital minimum_confidence max_symbol_exposure max_open_positions max_trade_allocation allow_short_selling quantity_decimals");
        Require(Number(config, "initial_capital") > 0 && Rate(config, "max_symbol_exposure") > 0
            && Rate(config, "max_trade_allocation") > 0 && Count(config, "max_open_positions", true) > 0
            && Count(config, "quantity_decimals") <= 8 && !Boolean(config, "allow_short_selling"));
        Rate(config, "minimum_confidence");
    }

    internal static AiBacktestCatalogItem ParseComparison(JsonElement benchmark, JsonElement comparison)
    {
        ValidateBenchmark(benchmark);
        Keys(comparison, "model_name start_date end_date symbols equity_dates initial_capital ai_final_value ai_total_return ai_max_drawdown benchmark_final_value benchmark_total_return benchmark_max_drawdown final_value_delta return_delta same_initial_capital same_period same_symbols same_equity_dates benchmark_method");
        var name = Name(comparison);
        var start = Day(comparison, "start_date");
        var end = Day(comparison, "end_date");
        var capital = Number(comparison, "initial_capital");
        var final = Number(comparison, "ai_final_value");
        var totalReturn = Number(comparison, "ai_total_return");
        var benchmarkReturn = Number(benchmark, "total_return");
        Require(capital > 0 && final > 0 && capital == Number(benchmark, "initial_capital")
            && start <= end && start == Day(benchmark, "start_date") && end == Day(benchmark, "end_date")
            && Symbols(comparison).SequenceEqual(Symbols(benchmark))
            && Boolean(comparison, "same_initial_capital") && Boolean(comparison, "same_period")
            && Boolean(comparison, "same_symbols") && Boolean(comparison, "same_equity_dates")
            && Text(comparison, "benchmark_method") == "equal_weight_buy_and_hold");
        ValidateEquityDates(comparison.GetProperty("equity_dates"), start, end);
        Close(totalReturn, final / capital - 1);
        Close(Number(comparison, "benchmark_final_value"), Number(benchmark, "final_value"));
        Close(Number(comparison, "benchmark_total_return"), benchmarkReturn);
        Close(Rate(comparison, "benchmark_max_drawdown"), Rate(benchmark, "max_drawdown"));
        Close(Number(comparison, "final_value_delta"), final - Number(benchmark, "final_value"));
        Close(Number(comparison, "return_delta"), totalReturn - benchmarkReturn);
        return new(name, null, start, end, capital, final, totalReturn, null, Rate(comparison, "ai_max_drawdown"), null,
            benchmarkReturn, Rate(benchmark, "max_drawdown"), Number(comparison, "return_delta"), "benchmark_comparison", null);
    }

    internal static void ValidateEquityDates(JsonElement array, DateOnly start, DateOnly end)
    {
        Require(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0);
        var dates = array.EnumerateArray().Select(item => Day(Text(item))).ToArray();
        Require(dates[0] == start && dates[^1] == end && dates.SequenceEqual(dates.Distinct().Order()));
    }

    private static void ValidateBenchmark(JsonElement benchmark)
    {
        Keys(benchmark, "initial_capital final_cash final_positions_value final_value total_return max_drawdown start_date end_date symbols symbol_count invested_symbols uninvested_symbols weighting_method entry_timing valuation_timing quantity_decimals commission slippage dividends_included price_adjustment_note");
        ValidateValue(benchmark, Number(benchmark, "initial_capital"), Number(benchmark, "final_value"), Number(benchmark, "total_return"));
        Rate(benchmark, "max_drawdown");
        var symbols = Symbols(benchmark);
        var invested = Symbols(benchmark, "invested_symbols", true);
        var uninvested = Symbols(benchmark, "uninvested_symbols", true);
        Require(Count(benchmark, "symbol_count", true) == symbols.Length
            && invested.Concat(uninvested).Distinct(StringComparer.Ordinal).Count() == symbols.Length
            && invested.Concat(uninvested).Order(StringComparer.Ordinal).SequenceEqual(symbols)
            && Day(benchmark, "start_date") <= Day(benchmark, "end_date")
            && Text(benchmark, "weighting_method") == "equal_weight"
            && Text(benchmark, "entry_timing") == "first observed session open strictly after start_date, at or before end_date"
            && Text(benchmark, "valuation_timing") == "EOD latest observed close <= equity date; no final liquidation"
            && Count(benchmark, "quantity_decimals") == 8 && Number(benchmark, "commission") == 0
            && Number(benchmark, "slippage") == 0 && !Boolean(benchmark, "dividends_included")
            && Text(benchmark, "price_adjustment_note") == "Prices as supplied by the cleaned dataset; no additional split adjustment.");
    }

    private static void ValidateValue(JsonElement report, decimal capital, decimal final, decimal totalReturn)
    {
        var cash = Number(report, "final_cash");
        var positions = Number(report, "final_positions_value");
        Require(capital > 0 && final > 0 && cash >= 0 && positions >= 0);
        Close(final, cash + positions);
        Close(totalReturn, final / capital - 1);
    }
}
