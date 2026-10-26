# AI risk/execution implementation plan

**Goal:** Complete #258 using existing risk and paper-execution services, with durable attempt outcomes.

**Spec:** `docs/superpowers/specs/2026-10-26-ai-risk-execution-design.md`.

1. Write orchestration tests against the real services/isolated fixture: BUY/SELL/HOLD, refusals, replay, failures and preservation of user data. Observe missing-contract failure.
2. Add the result/interface, attempt entity/mapping/DbSet and generated migration. Extract only the existing engine quote method for shared use; preserve validation/error mapping. Build/migrate only isolated outputs/databases.
3. Implement sequential orchestration, conditional journal transitions, technical-failure persistence, bounded cancellation completion and terminal-outcome reconciliation. Extend tests for malformed risk output, post-commit audit failure and concurrency.
4. Test and implement the explicit-ID operator command, database/migration/portfolio guards, DI registration, command-only log suppression and safe summaries. No HTTP writes or scheduler.
5. Document journal semantics, explicit migration/configuration, retry/cancellation limits and how #257 callers supply IDs. Run relevant and full backend checks, independent review and an isolated actual-command smoke; publish a PR against develop without merging or enabling operational trading.
