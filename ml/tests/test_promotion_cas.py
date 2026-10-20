"""The active model observed by an evaluation cannot be silently overwritten."""

from dataclasses import replace

import pytest

from test_model_versioning import models, register, registry
from stocklab_ml.versioning import ModelStatus, ModelVersionError


def test_cas_switches_only_expected_family_and_preserves_metadata(registry, models):
    first, second = register(registry, models[0]), register(registry, models[0])
    other = register(registry, models[1])
    registry.set_active_version(other.model_name, other.model_version)
    registry.set_active_version(first.model_name, first.model_version)
    promoted = registry.set_active_version_if_current(first.model_name, second.model_version, first.model_version)
    assert promoted == replace(second, status=ModelStatus.ACTIVE)
    assert registry.get_version(first.model_name, first.model_version) == first
    assert registry.get_active_version(other.model_name).model_version == other.model_version
    assert registry.get_version(first.model_name, first.model_version).status == ModelStatus.ARCHIVED


def test_stale_cas_does_not_write(registry, models):
    first, second = register(registry, models[0]), register(registry, models[0])
    registry.set_active_version(first.model_name, first.model_version)
    before = registry.path.read_bytes()
    with pytest.raises(ModelVersionError, match="Active.*changed"):
        registry.set_active_version_if_current(first.model_name, second.model_version, None)
    assert registry.path.read_bytes() == before


def test_cas_explicit_initialization_and_no_repeat_mutation(registry, models):
    first = register(registry, models[0])
    registry.set_active_version_if_current(first.model_name, first.model_version, None)
    before = registry.path.read_bytes()
    with pytest.raises(ModelVersionError, match="Active.*changed"):
        registry.set_active_version_if_current(first.model_name, first.model_version, None)
    assert registry.path.read_bytes() == before


def test_cas_checks_evaluated_immutable_records_inside_lock(registry, models):
    from uuid import uuid4
    first, second = register(registry, models[0]), register(registry, models[0])
    registry.set_active_version(first.model_name, first.model_version)
    before = registry.path.read_bytes()
    changed = replace(second, training_run_id=str(uuid4()))
    with pytest.raises(ModelVersionError, match="metadata/status changed"):
        registry.set_active_version_if_current(first.model_name, second.model_version, first.model_version,
                                               expected_records=(changed,))
    assert registry.path.read_bytes() == before
