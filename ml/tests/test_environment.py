"""Offline smoke tests for the StockLab Python environment."""

from importlib import import_module

import pytest


@pytest.mark.parametrize("module_name", ["numpy", "pandas", "sklearn", "stocklab_ml"])
def test_environment_import(module_name):
    """Each required module must load successfully in the test environment."""
    module = import_module(module_name)
    assert module.__name__ == module_name
