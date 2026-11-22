# semanticvr_ref

Explicit float64 ground-truth colour maths (linear sRGB → Machado CVD simulation, XYZ, Lab, CIEDE2000).
The C# and HLSL code in `packages/com.nayuraikar.semanticvr` is tested against golden vectors produced here.
colour-science is used only as a cross-check in the tests.

## Run the tests (from `python/`)

    source .venv/bin/activate
    pytest tests -q -s        # -s prints the max-abs-error report

Tests compare against colour-science on 10,000 random colours and the 34 published Sharma CIEDE2000 pairs
(`tests/sharma_pairs.py`).

## Regenerate goldens (from `python/`)

    python -m semanticvr_ref.make_golden

Writes `packages/com.nayuraikar.semanticvr/Tests/Runtime/golden.json` (seeded, deterministic, byte-identical on rerun).
Inputs are float32-exact; each expected value has a `*_f32` twin (float64 result rounded to float32).
Re-run and commit the JSON whenever `color.py` changes.

## Known colour-science quirk

For severities off the 0.1 grid, `colour.matrix_cvd_Machado2009` (0.4.7) extrapolates from the *upper*
segment (e.g. 0.15 uses the 0.2→0.3 slope), differing by up to ~0.095 per matrix entry.
`color.machado_matrix` interpolates between the two bracketing matrices instead. They agree exactly on the grid.
