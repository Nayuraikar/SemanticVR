"""Phase 1b palette check: dE00 (normal / deutan / protan, severity 1) for every colour pair of each scene group.

Run from python/:  python -m scripts.phase1b_palette   (or: python scripts/phase1b_palette.py)
Colours here must match packages/com.nayuraikar.semanticvr/Editor/DefaultClassLibrary.cs.
A pair is "confusable" when dE00(normal) >= 10 and dE00(sim) <= 15 (the SemanticGraphGizmos defaults).
"""
import itertools
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from semanticvr_ref import color as C  # noqa: E402

GROUPS = {
    "Cockpit": {
        "Warning.Red": "#E5322D", "Caution.Amber": "#F2A516", "Advisory.White": "#F1EFE9", "Advisory.Blue": "#2F7FD8",
        "Normal.Green": "#3DBB5C", "Text.Green": "#3DDC6A", "Text.Magenta": "#E040C8", "Text.Cyan": "#38D0E0",
        "Text.Amber": "#F2A516", "Text.Red": "#E5322D", "Papi.Red": "#E5322D", "Papi.White": "#F1EFE9",
    },
    "Medical": {
        "AlarmHigh.Red": "#E5322D", "AlarmMed.Yellow": "#F2D21E", "AlarmLow.Cyan": "#38C8D8",
        "Trace.Ecg": "#3DDC6A", "Trace.Spo2": "#38C8D8", "Trace.Bp": "#E5322D", "Trace.Temp": "#F2D21E",
        "Gas.O2": "#2E9E4F", "Gas.He": "#8A5A32", "Gas.Air": "#E8C21E", "Gas.N2O": "#2F5FA8", "Gas.CO2": "#8C9096", "Gas.Vac": "#F1EFE9",
        "Drug.Yellow": "#E8C21E", "Drug.Blue": "#2F5FA8", "Drug.Red": "#C62828", "Drug.Orange": "#E8761E",
        "Drug.Violet": "#7B4BA8", "Drug.Green": "#2E9E4F",
        "Pump.Green": "#3DBB5C", "Pump.Amber": "#F2A516", "Pump.Red": "#E5322D",
        "Skin.Light": "#E0B49A", "Tissue.Cyanosis": "#9C9FB8", "Tissue.Wound": "#B84A4A",
    },
}


def to_lin(h):
    v = np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)]) / 255.0
    return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)


def lab(h, t=None):
    rgb = to_lin(h)
    if t:
        rgb = np.clip(C.simulate_cvd(rgb, t, 1.0), 0, 1)
    return C.rgb_lin_to_lab(rgb)


def table(group, cols):
    rows = []
    for (na, a), (nb, b) in itertools.combinations(cols.items(), 2):
        rows.append((na, nb, float(C.ciede2000(lab(a), lab(b))),
                     float(C.ciede2000(lab(a, "deutan"), lab(b, "deutan"))),
                     float(C.ciede2000(lab(a, "protan"), lab(b, "protan")))))
    return rows


if __name__ == "__main__":
    print("cvd types:", C.CVD_TYPES)
    for g, cols in GROUPS.items():
        print(f"\n== {g}: confusable under deutan (orig>=10, sim<=15)")
        for na, nb, o, d, p in sorted(table(g, cols), key=lambda r: r[3]):
            if o >= 10 and d <= 15:
                print(f"  {na:18s} {nb:18s} normal {o:5.1f}  deutan {d:5.1f}  protan {p:5.1f}")
