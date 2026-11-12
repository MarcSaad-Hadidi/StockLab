# Local ML operation

This procedure operates the existing Python and backend workflows on the user's
PC. Keep the PC awake and the configured Windows user signed in. Normal HTTP
startup remains independent of ML operation; no cloud account is required.

Use a dedicated named LocalDB instance for the configured Windows user. Packaged
desktop applications can virtualize LocalDB's default profile directories: a task
may otherwise resolve an empty instance with the same default name. The local
setup uses `(localdb)\StockLabLocal`. Verify access from both the application and
the task context. Launch the HTTP API in the same native Windows task context as
the ML worker: launching it from a packaged desktop app can create conflicting
LocalDB runtime metadata even for the same named instance and Windows user.
The local `StockLab-Local-Api` task is started manually and has no logon trigger.
Moving an existing database requires a verified COPY_ONLY
backup, restore into an empty target and matching table counts/content checksums;
retain the original database and backup. Never initialize a replacement portfolio
to hide an instance mismatch.

## Existing workflows

- `stocklab_ml.operational_cycle --once`: fresh completed-session data, verified
  Active model, inference, backend risk/paper settlement and persisted valuation.
- `stocklab_ml.operational_cycle --loop`: sequential passes every configured
  interval, with one OS-owned runner lock and durable session journals.
- `stocklab_ml.retraining.cli --once`: a bounded refresh/training/publication
  check. New versions remain Archived; promotion uses the separate audited #78
  workflow. Rejected candidates preserve Active and its fitted artifact.

Use the private operator configuration described in
[AI Trader operations](../../frontend/AI_TRADER_OPERATIONS.md#run-the-full-local-paper-cycle-273).
Its backend DLL must point to the current build, not a retired scratch build.
The private launcher resolves the authorized Fallback key from existing User
Secrets into the Python process environment. No key belongs in task arguments,
config files, logs or this repository. Backend quote credentials remain separate.

The five-symbol commissioning wrapper reuses the cycle's immutable daily input
snapshot for retraining through `RetrainingDependencies.local_data`. It validates
the current session manifest and trims only rows beyond retraining's conservative
cutoff; a missing/invalid current input fails without a second provider batch.
The retraining logon trigger waits two minutes for the cycle to prepare inputs;
its existing daily trigger and three-day cadence remain. This avoids competing
five-request ML batches against the same eight-credit-per-minute Fallback key.
The general retraining CLI still supports its explicit provider-backed workflow.

The optional retraining config `"training_profile": "recent-history-v1"` reserves
the latest 50 supervised observation dates for evaluation and fits the preceding
four calendar years. It computes features from the complete immutable snapshot
before selecting fitted rows, preserving causal indicator warm-up and the full
bootstrap. Its holdout replaces the configured `test_fraction`; omitted/null
profiles retain the original fraction and legacy journal identity. Too little
history skips publication, and unknown profiles fail configuration validation.
Published versions remain Archived until a separately audited promotion.

After an explicit historical assessment, a private cycle config may select
`"risk_profile": "daily-multi-asset-v1"`. This pins a 0.65 directional-score floor,
20% symbol exposure, 10% per-order allocation, ten positions and no shorts in that
backend child process only. The HTTP service and unnamed cycles retain their
existing defaults (at least 0.70 for an unnamed cycle). It does not change the ML
BUY/SELL thresholds or manufacture calibrated probabilities. Unknown profiles or
mismatched limits fail before inference. Use a fresh state scope: schema-v2
journals pin the profile and cannot be replayed/resumed under another policy;
legacy schema-v1 journals remain valid for unnamed cycles. Historical activity
and profit are screening evidence, not prospective guarantees.

The runner and actual backend entry point explicitly default the command-line
profile to `baseline-v1`, so an ambient setting cannot silently opt an unnamed
cycle into the alternative policy. A new journal is rejected if matching durable
decisions already exist; restore the original journal rather than adopting those
decisions in a fresh directory. Keep one active runner/state scope per system
portfolio. Retain retired scopes only as recovery archives after checking their
terminal outcomes and stopping their workers. A new scope requires disjoint
decision identities; concurrent different-scope execution is unsupported.

## Windows tasks

The commissioned local setup uses two ML tasks under the current user's identity,
with limited privileges and no saved Windows password:

| Task | Trigger and behavior |
| --- | --- |
| `StockLab-ML-Cycle` | User logon; runs the existing loop every 30 minutes. |
| `StockLab-ML-Retraining` | User logon and daily at 19:30 Windows local time; the existing three-day cadence decides whether training is due. |

Tasks ignore overlapping starts and permit three bounded restarts after a process
failure. The host-specific launcher, configuration, logs, artifacts and journals
remain together in ignored private model directories. Retain them for recovery;
copying this Git checkout alone does not recreate a commissioned environment.

Use an installed stable PowerShell executable for task actions, such as Windows
PowerShell with compatible launchers. Avoid versioned Codex/WindowsApps runtime
paths: an application update can remove them and leave tasks unable to start.
Keep the machine's existing execution policy; do not add a policy bypass.

Inspect operation without reading or printing secrets:

```powershell
Get-ScheduledTask -TaskName 'StockLab-ML-*' | Select-Object TaskName, State
Get-ScheduledTaskInfo -TaskName 'StockLab-ML-Retraining'
```

Read the configured cycle state directory's `status.json` for the actual stage,
last successful timestamp, session and executed/rejected/HOLD counts. A Running
Windows task alone does not prove a successful pass. Consult the private
retraining journal/logs for its latest publication or controlled failure.

Request a clean stop and let the active pass finish before maintenance:

```powershell
$env:PYTHONPATH = "$PWD/ml/src"
ml/.venv/Scripts/python.exe -m stocklab_ml.operational_cycle --config <private-cycle-config.json> --stop
# After the worker has exited:
Start-ScheduledTask -TaskName 'StockLab-ML-Cycle'
```

Disable the logon task too if the stop must survive the next sign-in. Rebuild the
backend explicitly after source updates and keep its configured path current.
Never delete locks based on age or reset a journal to force a new session.

## Acceptance and data revisions

Verify an authentic one-shot pass, replay without additional SQL rows, clean
stop/restart, task-launched success and a retraining candidate publication with
the existing model retained unless its audited promotion is approved. A HOLD or
risk refusal is an ordinary outcome; do not change confidence thresholds to force
orders. Confirm persisted outputs through the authenticated AI Trader page when
signed in to the intended database.

An overlapping provider correction returns `DATA_REVISION_CONFLICT`. Preserve
the original bootstrap/journal and immutable source responses. Review the actual
revision, then prepare a corrected bootstrap at a new path and a new journal
scope using the same registry/artifact roots, as documented in the
[retraining contract](../README.md#new-observations-incremental-refresh-and-credit-budget).
Never silently rewrite the existing training lineage.

Run the offline suite from the repository root with
`ml/.venv/Scripts/python.exe -m pytest ml -q`; it excludes generated private
directories and supports root-level CLI subprocesses. These checks do not
simulate an actual Windows reboot, an unavailable future provider, or cloud
deployment. After changing Windows credentials, paths or machine power policy,
verify a real sign-in/restart before relying on unattended operation.
