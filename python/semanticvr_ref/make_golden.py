"""Generate golden test vectors for the Unity C# / HLSL colour code.

    python/.venv/bin/python -m semanticvr_ref.make_golden      (run from python/)

Output (deterministic; fixed seed, no timestamps):
    packages/com.nayuraikar.semanticvr/Tests/Runtime/golden.json

Conventions
-----------
* All RGB are LINEAR sRGB.  Inputs (rgb_lin, severity) are rounded to float32
  FIRST, so C# reads exactly the value the reference used.  Expected outputs are
  float64 computed from those inputs; each also has a "<name>_f32" twin that is
  the float64 result rounded to float32 (what a float32 implementation can at
  best reproduce).
* simulated_rgb is NOT clamped.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

from . import color as C

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "packages/com.nayuraikar.semanticvr/Tests/Runtime/golden.json"
SEED = 20240607
N_RANDOM = 1000


def f32(x):
    """float64 -> float32 -> float64 (exact round trip), as nested python floats."""
    return np.asarray(x, dtype=np.float64).astype(np.float32).astype(np.float64)


def lst(a):
    a = np.asarray(a, dtype=np.float64)
    return a.tolist() if a.ndim else float(a)


def cvd_case(rgb, cvd, sev, tag=None):
    rgb = f32(rgb)
    sev = float(f32(sev))
    sim = C.simulate_cvd(rgb, cvd, sev)
    lab0 = C.rgb_lin_to_lab(rgb)
    # Lab of the simulated colour: clamp to [0,1] is NOT applied (pure maths)
    lab1 = C.rgb_lin_to_lab(sim)
    de = C.ciede2000(lab0, lab1)
    case = {
        "rgb_lin": lst(rgb),
        "cvd_type": cvd,
        "severity": sev,
        "simulated_rgb": lst(sim), "simulated_rgb_f32": lst(f32(sim)),
        "lab_original": lst(lab0), "lab_original_f32": lst(f32(lab0)),
        "lab_simulated": lst(lab1), "lab_simulated_f32": lst(f32(lab1)),
        "de00_original_vs_simulated": float(de),
        "de00_original_vs_simulated_f32": float(f32(de)),
    }
    if tag:
        case["name"] = tag
    return case


def main() -> None:
    rng = np.random.default_rng(SEED)

    random_cases = []
    for _ in range(N_RANDOM):
        rgb = rng.random(3)
        cvd = C.CVD_TYPES[int(rng.integers(3))]
        sev = rng.random()
        random_cases.append(cvd_case(rgb, cvd, sev))

    # ---- edge cases ----
    colours = {
        "black": (0, 0, 0), "white": (1, 1, 1),
        "grey_0.05": (0.05,) * 3, "grey_0.18": (0.18,) * 3, "grey_0.5": (0.5,) * 3,
        "grey_0.8": (0.8,) * 3,
        "red": (1, 0, 0), "green": (0, 1, 0), "blue": (0, 0, 1),
        "yellow": (1, 1, 0), "cyan": (0, 1, 1), "magenta": (1, 0, 1),
        "near_black_dark_branch": (0.001, 0.002, 0.0005),
    }
    edge_cases = []
    for name, rgb in colours.items():
        for cvd in C.CVD_TYPES:
            for sev in (0.0, 1.0):
                edge_cases.append(cvd_case(rgb, cvd, sev, f"{name}/{cvd}/sev{sev:g}"))
    # exact grid + mid-segment severities (exercise matrix interpolation)
    for sev in (0.05, 0.1, 0.15, 0.5, 0.95):
        for cvd in C.CVD_TYPES:
            edge_cases.append(cvd_case((0.8, 0.3, 0.2), cvd, sev, f"interp/{cvd}/sev{sev:g}"))

    # ---- Sharma pairs ----
    sys.path.insert(0, str(REPO / "python/tests"))
    from sharma_pairs import SHARMA_PAIRS
    sharma = []
    for lab1, lab2, expected in SHARMA_PAIRS:
        l1, l2 = f32(lab1), f32(lab2)
        sharma.append({
            "lab1": lst(l1), "lab2": lst(l2),
            "de00_expected_published": expected,
            # our value on the float32-rounded inputs (what C# actually receives)
            "de00_reference": float(C.ciede2000(l1, l2)),
            "de00_reference_f32": float(f32(C.ciede2000(l1, l2))),
            # our value on the exact 4-dp inputs
            "de00_reference_f64_inputs": float(C.ciede2000(lab1, lab2)),
        })

    # ---- machado matrices (so matrix building can be tested in isolation) ----
    matrices = []
    for cvd in C.CVD_TYPES:
        for sev in (0.0, 0.05, 0.1, 0.25, 0.5, 0.75, 0.95, 1.0):
            m = C.machado_matrix(cvd, float(f32(sev)))
            matrices.append({"cvd_type": cvd, "severity": float(f32(sev)),
                             "matrix_row_major": lst(m.reshape(9)),
                             "matrix_row_major_f32": lst(f32(m.reshape(9)))})

    doc = {
        "meta": {
            "seed": SEED,
            "description": "Golden vectors from python/semanticvr_ref (float64). All RGB are linear sRGB.",
            "white_d65_xyz": lst(C.WHITE_D65),
            "rgb_to_xyz": lst(C.RGB_TO_XYZ),
            "ciede2000": "kL=kC=kH=1",
            "machado_interpolation": "linear between bracketing 0.1-grid matrices",
            "notes": "inputs are float32-exact; *_f32 fields are float64 results rounded to float32",
        },
        "random_cases": random_cases,
        "edge_cases": edge_cases,
        "sharma_pairs": sharma,
        "machado_matrices": matrices,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(doc, indent=1, sort_keys=False) + "\n")
    print(f"wrote {OUT.relative_to(REPO)}: {len(random_cases)} random, "
          f"{len(edge_cases)} edge, {len(sharma)} sharma, {len(matrices)} matrices")


if __name__ == "__main__":
    main()
