# Local ML operation

This procedure operates the existing Python and backend workflows on the user's
PC. Keep the PC awake and the configured Windows user signed in. Normal HTTP
startup remains independent of ML operation; no cloud account is required.

Use a dedicated named LocalDB instance for the configured Windows user. Packaged
desktop applications can virtualize LocalDB's default profile directories: a task
may otherwise resolve an empty instance with the same default name. The local
setup uses `(localdb)\StockLabLocal`. Verify access from both the application and
the task context. Moving an existing database requires a verified COPY_ONLY
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

## Windows tasks

The commissioned local setup uses two tasks under the current user's identity,
with limited privileges and no saved Windows password:

| Task | Trigger and behavior |
| --- | --- |
| `StockLab-ML-Cycle` | User logon; runs the existing loop every 30 minutes. |
| `StockLab-ML-Retraining` | User logon and daily at 19:30 Windows local time; the existing three-day cadence decides whether training is due. |

Tasks ignore overlapping starts and permit three bounded restarts after a process
failure. The host-specific launcher, configuration, logs, artifacts and journals
remain together in ignored private model directories. Retain them for recovery;
copying this Git checkout alone does not recreate a commissioned environment.

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
