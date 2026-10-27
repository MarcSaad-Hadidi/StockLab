using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.Api.Operations;

/// <summary>One guarded paper cycle; private checkpoints supplement existing durable decision/order identities.</summary>
public static class AiTradingCycleCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration,
        TextWriter output, CancellationToken cancellationToken = default)
    {
        AiTradingCycleJournal? journal = null;
        string? journalPath = null;
        FileStream? lease = null;
        var stage = "PREFLIGHT";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var db = provider.GetRequiredService<StockLabDbContext>();
            var database = configuration["AiTradingCycle:ExpectedDatabase"];
            stage = "DATABASE_TARGET";
            if (string.IsNullOrWhiteSpace(database) || database != db.Database.GetDbConnection().Database)
                throw new InvalidOperationException();
            stage = "DATABASE_CONNECTION";
            if (!await db.Database.CanConnectAsync(cancellationToken)) throw new InvalidOperationException();
            stage = "MIGRATIONS";
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()) throw new InvalidOperationException();
            stage = "PROVIDER";
            if (configuration["MarketData:Provider"] != "TwelveData") throw new InvalidOperationException();
            stage = "POLICY";
            var riskPolicy = provider.GetRequiredService<IOptions<AiRiskOptions>>().Value;
            if (!riskPolicy.IsValid() || riskPolicy.MinimumConfidence < .70m) throw new InvalidOperationException();
            stage = "PORTFOLIO";
            await provider.GetRequiredService<IAiTraderPortfolioService>().GetStateAsync(cancellationToken, initializeIfMissing: false);
            stage = "MODEL";
            var modelName = configuration["AiTradingCycle:ModelName"];
            var modelVersion = configuration["AiTradingCycle:ModelVersion"];
            var symbols = provider.GetRequiredService<IOptions<AiDecisionPipelineOptions>>().Value.Symbols;
            if (symbols is null || symbols.Length is < 1 or > 10 || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length
                || symbols.Any(s => string.IsNullOrWhiteSpace(s) || s != s.ToUpperInvariant() || s.Length > 32))
                throw new InvalidOperationException();
            symbols = symbols.Order(StringComparer.Ordinal).ToArray();
            async Task VerifyModel()
            {
                var models = await provider.GetRequiredService<IAiModelCatalogReadService>().GetAsync(cancellationToken);
                if (models.Count(m => m.ModelName == modelName && m.ModelVersion == modelVersion && m.Status == "Active") != 1)
                    throw new InvalidOperationException();
            }
            await VerifyModel();
            if (configuration.GetValue<bool>("AiTradingCycle:CheckOnly"))
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new { status = "READY", stage = "PREFLIGHT" }));
                return 0;
            }
            stage = "INPUTS";
            var clock = provider.GetRequiredService<TimeProvider>();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            DateOnly ParseDate(string key) => DateOnly.ParseExact(configuration[key]!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var session = ParseDate("AiTradingCycle:DecisionDate");
            var prepared = ParseDate("AiTradingCycle:PreparedUtcDate");
            var dataset = configuration["AiDecisionPipeline:DatasetPath"];
            var hash = configuration["AiTradingCycle:DatasetSha256"];
            var directory = configuration["AiTradingCycle:StateDirectory"];
            if (prepared != today || session >= today || session < today.AddDays(-7)
                || string.IsNullOrWhiteSpace(dataset) || !Path.IsPathFullyQualified(dataset)
                || string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
                || hash is null || !Regex.IsMatch(hash, "\\A[a-f0-9]{64}\\z"))
                throw new InvalidOperationException();
            void VerifyInput()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) != prepared
                    || new FileInfo(dataset).Length > 64 * 1024 * 1024
                    || File.GetAttributes(dataset).HasFlag(FileAttributes.ReparsePoint)
                    || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dataset))) != hash)
                    throw new InvalidOperationException();
            }
            VerifyInput();
            stage = "JOURNAL";
            Directory.CreateDirectory(directory);
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException();
            lease = new FileStream(Path.Combine(directory, "cycle.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            stage = "RECONCILIATION";
            foreach (var previousPath in Directory.EnumerateFiles(directory, "session-*.json").Order(StringComparer.Ordinal))
            {
                var previous = AiTradingCycleJournal.Read(previousPath);
                if (previous.Database != database || previous.StartedAtUtc > clock.GetUtcNow().UtcDateTime
                    || Path.GetFileName(previousPath) != "session-" + previous.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json")
                    throw new InvalidOperationException();
                if (previous.Session >= session || previous.Stage is "COMPLETED" or "ABANDONED") continue;
                var previousRows = await db.AiDecisions.AsNoTracking().Where(d => d.DecisionDate == previous.Session
                    && d.ModelName == previous.ModelName && d.ModelVersion == previous.ModelVersion
                    && previous.Symbols.Contains(d.Symbol)).ToArrayAsync(cancellationToken);
                if (previousRows.Select(d => d.Symbol).Distinct(StringComparer.Ordinal).Count() != previousRows.Length
                    || previous.DecisionIds.Length > 0 && (previousRows.Length != previous.DecisionIds.Length
                        || previousRows.Any(d => !previous.DecisionIds.Contains(d.Id))))
                    throw new InvalidOperationException();
                var previousIds = previousRows.Select(d => d.Id).ToArray();
                var previousOutcomes = await provider.GetRequiredService<IAiDecisionOutcomeReadService>().GetAsync(previousIds, cancellationToken);
                if (previousOutcomes.Count != previousIds.Length) throw new InvalidOperationException();
                var terminal = previousIds.Length == previous.Symbols.Length
                    && previousOutcomes.Values.All(o => o.ExecutionStatus is "EXECUTED" or "REJECTED" or "HOLD");
                // Reconcile committed state at today's quotes; never execute a stale unresolved signal.
                if (terminal || previousOutcomes.Values.Any(o => o.ExecutionStatus == "EXECUTED"))
                    await provider.GetRequiredService<IAiTraderValuationHistoryService>().CaptureOnceAsync(previous.CaptureId, cancellationToken);
                (previous with { DecisionIds = previousIds, Stage = terminal ? "COMPLETED" : "ABANDONED",
                    FailureCode = terminal ? null : "STALE_UNRESOLVED" }).Save(previousPath);
            }
            stage = "JOURNAL";
            journalPath = Path.Combine(directory, "session-" + session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
            if (File.Exists(journalPath))
            {
                var saved = AiTradingCycleJournal.Read(journalPath);
                if (saved.Database != database || saved.Session != session || saved.ModelName != modelName
                    || saved.ModelVersion != modelVersion || saved.DatasetSha256 != hash || !saved.Symbols.SequenceEqual(symbols)
                    || saved.StartedAtUtc > clock.GetUtcNow().UtcDateTime || saved.Stage == "ABANDONED")
                    throw new InvalidOperationException();
                journal = saved;
            }
            else
            {
                journal = new(1, database, session, modelName!, modelVersion!, symbols, hash, [],
                    Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, "INFERENCE");
                journal.Save(journalPath);
            }
            var replay = journal.Stage == "COMPLETED";
            stage = "INFERENCE";
            if (journal.DecisionIds.Length == 0)
            {
                VerifyInput();
                await VerifyModel();
                var decisions = await provider.GetRequiredService<IAiDecisionPipelineService>().RunAsync(session, cancellationToken);
                if (decisions.Count != symbols.Length || decisions.Any(d => d.DecisionDate != session
                    || d.ModelName != modelName || d.ModelVersion != modelVersion)
                    || !decisions.Select(d => d.Symbol).Order(StringComparer.Ordinal).SequenceEqual(symbols))
                    throw new InvalidOperationException();
                journal = journal with { DecisionIds = decisions.Select(d => d.Id).ToArray(), Stage = "EXECUTION", FailureCode = null };
                journal.Save(journalPath);
            }
            var rows = await db.AiDecisions.AsNoTracking().Where(d => journal.DecisionIds.Contains(d.Id)).ToArrayAsync(cancellationToken);
            if (rows.Length != symbols.Length || rows.Any(d => d.DecisionDate != session || d.ModelName != modelName || d.ModelVersion != modelVersion)
                || !rows.Select(d => d.Symbol).Order(StringComparer.Ordinal).SequenceEqual(symbols))
                throw new InvalidOperationException();
            if (!replay)
            {
                stage = "EXECUTION";
                foreach (var id in journal.DecisionIds)
                {
                    VerifyInput();
                    await VerifyModel();
                    var result = await provider.GetRequiredService<IAiRiskExecutionService>().ExecuteAsync(id, cancellationToken);
                    if (result.Status is not ("EXECUTED" or "REJECTED" or "HOLD")) throw new InvalidOperationException();
                }
                stage = "PERFORMANCE";
                journal = journal with { Stage = stage, FailureCode = null };
                journal.Save(journalPath);
                await provider.GetRequiredService<IAiTraderValuationHistoryService>().CaptureOnceAsync(journal.CaptureId, cancellationToken);
                journal = journal with { Stage = "COMPLETED" };
                journal.Save(journalPath);
            }
            stage = "COMPLETED";
            var outcomes = await provider.GetRequiredService<IAiDecisionOutcomeReadService>().GetAsync(journal.DecisionIds, cancellationToken);
            if (outcomes.Count != symbols.Length || outcomes.Values.Any(o => o.ExecutionStatus is not ("EXECUTED" or "REJECTED" or "HOLD"))
                || !await db.AiTraderValuationSnapshots.AnyAsync(p => p.CaptureId == journal.CaptureId, cancellationToken))
                throw new InvalidOperationException();
            await output.WriteLineAsync(JsonSerializer.Serialize(new { status = stage, stage, session = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                executed = outcomes.Values.Count(o => o.ExecutionStatus == "EXECUTED"), rejected = outcomes.Values.Count(o => o.ExecutionStatus == "REJECTED"),
                hold = outcomes.Values.Count(o => o.ExecutionStatus == "HOLD"), replay }));
            return 0;
        }
        catch (Exception)
        {
            var status = cancellationToken.IsCancellationRequested ? "CANCELLED" : "FAILED";
            if (journal is not null && journalPath is not null)
                try { (journal with { FailureCode = status }).Save(journalPath); } catch (Exception) { }
            await output.WriteLineAsync(JsonSerializer.Serialize(new { status, stage }));
            return 1;
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
    }
}
