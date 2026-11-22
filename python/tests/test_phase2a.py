"""The NightCity design targets (docs/design/phase2a.md): which pairs are deutan-confusable in which lighting preset."""
import importlib.util
from pathlib import Path

import numpy as np
import pytest

_spec = importlib.util.spec_from_file_location("phase2a_nightcity", Path(__file__).resolve().parents[1] / "scripts" / "phase2a_nightcity.py")
P = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(P)

E = {k: P.irradiance(p) for k, p in P.PRESETS.items()}


def flags(a, b):
    return tuple(P.confusable(*P.pair_stats(P.SCENE[a], P.SCENE[b], E[k])) for k in ("Day", "Night"))


@pytest.mark.parametrize("a,b,day,night", [
    ("Neon.Red", "Shop.Green", True, False),
    ("Shop.Yellow", "Neon.Amber", True, False),
    ("Shop.Green", "Shop.Pink", False, True),
    ("Shop.Violet", "Shop.Teal", False, True),
    ("Shop.Blue", "Shop.Teal", False, True),
    ("Shop.Blue", "Shop.Violet", True, True),
    ("Shop.Red", "Shop.Green", True, True),
])
def test_design_pairs(a, b, day, night):
    assert flags(a, b) == (day, night)


def test_flat_ambient_irradiance_is_ambient_plus_lambert():
    # the sun/moon term: light colour * intensity * max(0, n.l); n = (0,0,-1) faces the viewer, the light comes from the viewer side
    p = P.PRESETS["Day"]
    to_light = -P.rot_forward(p["pitch"], p["yaw"])
    expect = P.to_lin(p["ambient"]) + P.to_lin(p["sun"]) * p["sun_i"] * max(0.0, -to_light[2])
    assert np.allclose(P.irradiance(p), expect)
    assert to_light[2] < 0  # lit from the front
