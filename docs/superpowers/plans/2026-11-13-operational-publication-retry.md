# Windows operational snapshot publication fix

**Goal:** Make the existing local ML cycle tolerate the observed transient Windows directory-publication denial without overwriting data, advancing the user's complete local ML objective.

**Evidence:** Unchanged legacy operational-cycle tests fail intermittently at DATA after all source/manifest files have been written. A transparent private test-only probe captures `Path.rename(.partial→session)` raising `PermissionError`, errno13/WinError5, destination absent, source path length178. Isolated retries pass; the cause of the Windows denial/lock is unknown. Do not attribute it to antivirus, indexing or path length without evidence. Original failing logs/probe evidence remain retained.

**Scope:** `ml/src/stocklab_ml/operational_data.py`, NEW `ml/tests/test_operational_publication.py` and relevant ML operations documentation. These paths are outside the53-source research and84-source snapshot closures. Preserve the old operational-cycle tests, Active/risk/configuration gates, model/data contracts and all actual inputs. No provider/database/worker operation is authorized by a unit test or code commit.

## Minimal behavior

Extract the existing directory publication into one small helper called once by `prepare_cycle_dataset`. Keep the partial and destination in the same expected parent. Refuse an existing destination before each attempt and after any denial; do not overwrite, merge, repair or delete either directory. Retry ONLY on Windows `PermissionError` with winerror5 or32, while the original source directory still exists and destination is absent. Re-raise unrelated errors immediately and the original denial after a fixed short bounded backoff. A permanent failure preserves the partial evidence. No infinite loop, global OS patch or general retry framework.

Use a fixed small exponential delay matching the existing atomic-writer pattern; record the exact attempt count/delays in code/tests before verification. Guard correctness/no-overwrite takes precedence over making a flaky test pass.

## Verification

- [ ] New synthetic regression first demonstrates the missing retry. Test5/32→success, bounded permanent denial/preserved source, unrelated errors, non-Windows denial, destination appearing during backoff and no overwrite/repair/cleanup. Mock waits in these tests; do not induce a real permission change in user data.
- [ ] Implement only the helper/one caller change. Independently review retries and no-overwrite/error preservation, then run new tests and the unchanged operational-cycle suite in a new owned test directory. Preserve prior failures; a passing rerun is not claimed to explain the underlying Windows lock.
- [ ] Run the appropriate full ML checks alongside the new diagnostic integration, record actual outcomes and publish in the draft PR. No actual task restart/data fetch/database or model activation.

This fix addresses one verified publication failure mode. It does not commission HGBT28 or demonstrate >20% returns, and the full goal remains active.
