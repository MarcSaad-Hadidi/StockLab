# AI decision pipeline implementation plan

**Goal:** Complete #257 by invoking verified inference and persisting original, replay-safe decisions.

**Spec:** `docs/superpowers/specs/2026-10-25-ai-decision-pipeline-design.md`.

**Architecture:** Python owns model selection, features, inference and signal policy. Infrastructure owns the bounded process bridge and recording service; the existing API entry point provides an explicit operator command. Reuse existing artifact/history contracts and add no dependency or migration.

1. Add `ml/tests/test_decision_pipeline.py` with real model fixtures; demonstrate the missing module failure, then implement `ml/src/stocklab_ml/decision_pipeline.py` and verify causality, identity, trust and safe CLI errors.
2. Add inference DTO/provider and pipeline interfaces in Application. Add recording tests, then Infrastructure service using stable IDs and the existing history validator. Preserve per-record replay/conflict behavior and validate the full batch first.
3. Test and implement process options, argument handling, strict JSON and bounded concurrent stream reads. Fail safely on exit errors, cancellation and timeout. Keep Python optional during normal API startup.
4. Test and implement the `--run-ai-decisions` operator command, database-name/migration guard and DI registration. Its command mode starts no listener/hosted workers and exposes only sanitized errors.
5. Document the required configuration, explicit artifact creation/activation prerequisites and one-cycle invocation. Use separate build artifacts so the currently running local API remains available.
6. Run relevant and full offline suites, a real-process local smoke and independent review. Commit/push a PR against develop; do not merge it or activate operational models.
