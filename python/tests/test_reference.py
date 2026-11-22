"""Cross-check the explicit reference maths against colour-science."""
import warnings

import colour
import numpy as np
import pytest

from semanticvr_ref import color as C
from sharma_pairs import SHARMA_PAIRS

warnings.filterwarnings("ignore")  # colour's tritanomaly usage warning

N = 10_000
CS_NAME = {"protan": "Protanomaly", "deutan": "Deuteranomaly", "tritan": "Tritanomaly"}
SRGB = colour.RGB_COLOURSPACES["sRGB"]
ERRORS = {}  # collected max abs errors, printed in the summary


def _record(name, err):
    ERRORS[name] = max(ERRORS.get(name, 0.0), float(err))


@pytest.fixture(scope="module")
def rgb():
    return np.random.default_rng(12345).random((N, 3))


def test_xyz(rgb):
    ref = colour.RGB_to_XYZ(rgb, SRGB, apply_cctf_decoding=False,
                            chromatic_adaptation_transform=None)
    err = np.abs(C.linear_rgb_to_xyz(rgb) - ref).max()
    _record("rgb_lin->XYZ", err)
    assert err < 1e-12


def test_lab(rgb):
    xyz = C.linear_rgb_to_xyz(rgb)
    ref = colour.XYZ_to_Lab(xyz, SRGB.whitepoint)
    err = np.abs(C.xyz_to_lab(xyz) - ref).max()
    _record("XYZ->Lab", err)
    assert err < 1e-9
    assert np.abs(C.rgb_lin_to_lab(rgb) - ref).max() < 1e-9


def test_lab_dark_branch():
    # exercise the linear (t <= epsilon) branch explicitly
    rgb = np.random.default_rng(1).random((N, 3)) * 0.005
    xyz = C.linear_rgb_to_xyz(rgb)
    err = np.abs(C.xyz_to_lab(xyz) - colour.XYZ_to_Lab(xyz, SRGB.whitepoint)).max()
    _record("XYZ->Lab (dark)", err)
    assert err < 1e-9


def test_ciede2000(rgb):
    lab = C.rgb_lin_to_lab(rgb)
    lab2 = C.rgb_lin_to_lab(np.random.default_rng(777).random((N, 3)))
    ref = colour.delta_E(lab, lab2, method="CIE 2000")
    err = np.abs(C.ciede2000(lab, lab2) - ref).max()
    _record("CIEDE2000", err)
    assert err < 1e-9


def test_ciede2000_close_colours(rgb):
    # small differences are the regime that matters for CVD simulation
    lab = C.rgb_lin_to_lab(rgb)
    lab2 = lab + np.random.default_rng(5).normal(scale=1.0, size=lab.shape)
    ref = colour.delta_E(lab, lab2, method="CIE 2000")
    err = np.abs(C.ciede2000(lab, lab2) - ref).max()
    _record("CIEDE2000 (close)", err)
    assert err < 1e-9


@pytest.mark.parametrize("i", range(34))
def test_sharma(i):
    lab1, lab2, expected = SHARMA_PAIRS[i]
    got = float(C.ciede2000(lab1, lab2))
    _record("Sharma (vs published, 4dp)", abs(got - expected))
    assert abs(got - expected) < 5e-5
    # symmetric in its arguments
    assert abs(float(C.ciede2000(lab2, lab1)) - got) < 1e-12


@pytest.mark.parametrize("cvd", C.CVD_TYPES)
def test_machado_matrix_on_grid(cvd):
    """On the published 0.1 grid we must equal colour exactly."""
    for k in range(11):
        s = k / 10.0
        err = np.abs(C.machado_matrix(cvd, s) - colour.matrix_cvd_Machado2009(CS_NAME[cvd], s)).max()
        _record("Machado matrix (grid)", err)
        assert err < 1e-12


@pytest.mark.parametrize("cvd", C.CVD_TYPES)
def test_simulate_cvd_on_grid(cvd, rgb):
    for s in (0.0, 0.3, 0.7, 1.0):
        ref = np.einsum("ij,nj->ni", colour.matrix_cvd_Machado2009(CS_NAME[cvd], s), rgb)
        err = np.abs(C.simulate_cvd(rgb, cvd, s) - ref).max()
        _record("simulate_cvd (grid)", err)
        assert err < 1e-12


@pytest.mark.parametrize("cvd", C.CVD_TYPES)
def test_machado_off_grid_is_bracketing_interpolation(cvd):
    """Off-grid severities: ours = linear blend of bracketing matrices.
    colour 0.4.7 extrapolates from the upper segment instead (documented quirk);
    we record the discrepancy but do not require equality."""
    rng = np.random.default_rng(9)
    worst = 0.0
    for s in rng.random(500):
        m = C.machado_matrix(cvd, s)
        lo, hi = int(s * 10), min(int(s * 10) + 1, 10)
        t = s * 10 - lo
        expect = (1 - t) * C.machado_matrix(cvd, lo / 10) + t * C.machado_matrix(cvd, hi / 10)
        assert np.abs(m - expect).max() < 1e-12
        worst = max(worst, np.abs(m - colour.matrix_cvd_Machado2009(CS_NAME[cvd], s)).max())
    _record(f"Machado matrix OFF-grid vs colour ({cvd}) [known colour quirk]", worst)


def test_machado_properties():
    for cvd in C.CVD_TYPES:
        assert np.allclose(C.machado_matrix(cvd, 0.0), np.eye(3), atol=1e-12)
        # rows sum to ~1: neutral greys stay neutral
        for s in (0.25, 0.55, 1.0):
            assert np.allclose(C.machado_matrix(cvd, s).sum(axis=1), 1.0, atol=2e-3)
    with pytest.raises(ValueError):
        C.machado_matrix("nope", 0.5)


def test_zz_report(capsys):
    with capsys.disabled():
        print("\n\n=== max abs errors vs colour-science / published ===")
        for k, v in ERRORS.items():
            print(f"  {k:70s} {v:.3e}")
