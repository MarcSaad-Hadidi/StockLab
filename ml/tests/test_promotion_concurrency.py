"""Two independent Windows/POSIX processes must never win the same expected Active."""

from dataclasses import replace
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from uuid import uuid4

import pytest

from stocklab_ml.versioning import ModelStatus
from stocklab_ml.artifacts.local_storage import artifact_paths
from test_model_promotion import api, setup, trained


WORKER = r'''
import json, socket, sys, time
from datetime import datetime
from pathlib import Path
from stocklab_ml.artifacts.local_storage import artifact_paths
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.promotion import *
from stocklab_ml.versioning import ActiveVersionConflictError, ModelVersionRegistry
from test_retraining import history
def forbidden(*args, **kwargs):
    raise AssertionError("Network forbidden")
socket.socket.connect = forbidden
socket.socket.connect_ex = forbidden
root, candidate, evaluation_id, mode = sys.argv[1:]
root = Path(root)
registry = ModelVersionRegistry(root / "registry.json")
name = "logistic_regression_baseline"
Path(root / (candidate + ".ready")).touch()
deadline = time.monotonic() + 30
while not (root / "go").exists():
    if time.monotonic() > deadline:
        raise RuntimeError("No parent barrier")
    time.sleep(.01)
try:
    if mode == "cas":
        registry.set_active_version_if_current(name, candidate, "v000001")
    else:
        store = PromotionAuditStore(root / "audit")
        decision = store.load_evaluation(evaluation_id)
        request = ModelPromotionRequest(name, candidate, None,
            clean_historical_dataset(history(220), as_of_date="2024-11-01"),
            ModelPromotionPolicy(minimum_accuracy=0, minimum_f1=0),
            datetime.fromisoformat(decision.evaluated_at_utc), evaluation_id,
            "2024-07-01", "2024-10-31", initialize=True)
        apply_model_promotion(decision, request=request, registry=registry, audit_store=store,
            candidate_artifact=artifact_paths(name, candidate, root / "artifacts"), trusted_source=True)
    print("APPLIED")
except (ActiveVersionConflictError, PromotionConflictError):
    print("CONFLICT")
'''


def race(root, candidates, evaluation_ids, mode):
    env = os.environ | {"PYTHONPATH": os.pathsep.join(str(Path(__file__).resolve().parents[1] / part)
                                                     for part in ("src", "tests"))}
    processes = [subprocess.Popen([sys.executable, "-c", WORKER, str(root), candidate, evaluation_id, mode],
                  env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                 for candidate, evaluation_id in zip(candidates, evaluation_ids, strict=True)]
    try:
        deadline = time.monotonic() + 30
        while not all((root / (candidate + ".ready")).exists() for candidate in candidates):
            assert time.monotonic() < deadline, "Workers did not reach the race barrier"
            time.sleep(.01)
        (root / "go").touch()
        results = [process.communicate(timeout=40) for process in processes]
        assert all(process.returncode == 0 for process in processes), results
        assert sorted(stdout.strip() for stdout, _ in results) == ["APPLIED", "CONFLICT"]
    finally:
        for process in processes:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)


@pytest.mark.parametrize("mode", ["cas", "apply"])
def test_two_processes_cannot_overwrite_each_others_promotion(setup, trained, mode):
    request, kwargs, records = setup(active=mode == "cas")
    registry = kwargs["registry"]
    root = registry.path.parent
    if mode == "cas":
        third = registry.register_version(trained[0].baseline_result, training_run_id=str(uuid4()),
                                           trained_at_utc=request.evaluation_timestamp_utc)
        race(root, [request.candidate_version, third.model_version], ["unused", "unused"], mode)
    else:
        request = replace(request, initialize=True,
            promotion_policy=api().ModelPromotionPolicy(minimum_accuracy=0, minimum_f1=0))
        first = api().evaluate_model_promotion(request, **kwargs)
        second_request = replace(request, evaluation_id=str(uuid4()), candidate_version="v000001")
        second = api().evaluate_model_promotion(second_request,
            **(kwargs | {"candidate_artifact": artifact_paths(request.model_name, "v000001", root / "artifacts")}))
        assert first.decision == second.decision == "APPROVED"
        race(root, [request.candidate_version, second_request.candidate_version],
             [request.evaluation_id, second_request.evaluation_id], mode)
    assert sum(record.status == ModelStatus.ACTIVE for record in registry.list_versions(request.model_name)) == 1
