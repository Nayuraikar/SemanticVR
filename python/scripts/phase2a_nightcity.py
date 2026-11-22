"""Phase 2A: pick NightCity colours so some pairs are deutan-confusable in ONE lighting preset only.

Run from python/:  python scripts/phase2a_nightcity.py [--search]

Lit colour model (identical to LitColorPredictor.cs, linear space throughout):
    lit = saturate(albedo_lin * E + emission_lin),   E = ambient_flat + sun_colour * intensity * max(0, N.L)
    emission_lin = albedo_lin * emission_intensity   (what SemanticTag.EmissionColor uploads)
NightCity uses AmbientMode.Flat so the ambient SH is the constant colour c (L0 only) and E is exact here; the other
scenes use Unity's trilight probe, which only the Unity-side validation covers.
A pair is confusable under deutan (severity 1) when dE00(normal) >= 10 and dE00(deutan) <= 15 (SemanticGraphGizmos defaults),
both computed on the lit, clamped colours. Design normal: a facade facing the viewer, n = (0, 0, -1).
"""
import itertools
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from semanticvr_ref import color as C  # noqa: E402


def to_lin(h):
    v = np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)]) / 255.0
    return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)


def rot_forward(pitch_deg, yaw_deg):
    """Direction a light with Unity rotation Euler(pitch, yaw, 0) points along (its +Z axis)."""
    p, y = math.radians(pitch_deg), math.radians(yaw_deg)
    return np.array([math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p)])


# Presets: must match the presets in Scene05NightCity.cs
PRESETS = {
    "Day": dict(sun="#FFF1DC", sun_i=1.25, pitch=48.0, yaw=-32.0, ambient="#8FA0B8"),
    "Night": dict(sun="#7F9CFF", sun_i=0.12, pitch=35.0, yaw=-32.0, ambient="#6B4426"),
}
NORMAL = np.array([0.0, 0.0, -1.0])


def irradiance(p, n=NORMAL):
    to_light = -rot_forward(p["pitch"], p["yaw"])
    return to_lin(p["ambient"]) + to_lin(p["sun"]) * p["sun_i"] * max(0.0, float(n @ to_light))


def lit(hex_, emission, E):
    a = to_lin(hex_)
    return np.clip(a * E + a * emission, 0.0, 1.0)


def de(rgb_a, rgb_b, cvd=None):
    if cvd:
        rgb_a = np.clip(C.simulate_cvd(rgb_a, cvd, 1.0), 0, 1)
        rgb_b = np.clip(C.simulate_cvd(rgb_b, cvd, 1.0), 0, 1)
    return float(C.ciede2000(C.rgb_lin_to_lab(rgb_a), C.rgb_lin_to_lab(rgb_b)))


def pair_stats(a, b, E):
    la, lb = lit(*a, E), lit(*b, E)
    return de(la, lb), de(la, lb, "deutan")


def confusable(o, s):
    return o >= 10.0 and s <= 15.0


# (name, hex, emission) used by the scene. Painted shop signs (emission 0) and neon tubes (emission > 0).
SCENE = {
    "Shop.Red": ("#C62828", 0.0), "Shop.Green": ("#2E9E4F", 0.0), "Shop.Olive": ("#8C8A2E", 0.0),
    "Shop.Orange": ("#E8761E", 0.0), "Shop.Blue": ("#2F5FA8", 0.0), "Shop.Violet": ("#7B4BA8", 0.0),
    "Shop.Teal": ("#2A8F8A", 0.0), "Shop.Pink": ("#D9739F", 0.0), "Shop.Yellow": ("#E8C21E", 0.0),
    "Neon.Red": ("#E5322D", 1.5), "Neon.Green": ("#3DDC6A", 1.5), "Neon.Amber": ("#F2A516", 1.5),
    "Neon.Cyan": ("#38D0E0", 1.5), "Neon.Magenta": ("#E040C8", 1.5),
}


def report(entries, title):
    Es = {k: irradiance(p) for k, p in PRESETS.items()}
    print(f"\n{title}")
    print("irradiance E (linear RGB) at n=(0,0,-1):", {k: np.round(v, 3).tolist() for k, v in Es.items()})
    print(f"{'pair':28s} " + " ".join(f"{k + ' orig/deut':>18s}" for k in PRESETS) + "  verdict")
    rows = []
    for (na, (ha, ea)), (nb, (hb, eb)) in itertools.combinations(entries.items(), 2):
        st = {k: pair_stats((ha, ea), (hb, eb), E) for k, E in Es.items()}
        flags = {k: confusable(*v) for k, v in st.items()}
        verdict = "both" if all(flags.values()) else ("Day only" if flags["Day"] else ("Night only" if flags["Night"] else "-"))
        rows.append((na, nb, st, verdict))
    for na, nb, st, v in rows:
        if v != "-":
            print(f"{na + ' / ' + nb:28s} " + " ".join(f"{st[k][0]:8.1f} /{st[k][1]:7.1f}  " for k in PRESETS) + f"  {v}")
    return rows


def search():
    """Brute-force a broad candidate palette for the cleanest one-sided flips (margin = distance to the thresholds)."""
    cand = {
        "red": "#C62828", "brick": "#9C3A2B", "orange": "#E8761E", "amber": "#F2A516", "yellow": "#E8C21E", "olive": "#8C8A2E",
        "lime": "#8FB04A", "green": "#2E9E4F", "forest": "#2F6F3A", "teal": "#2A8F8A", "cyan": "#38C8D8", "sky": "#5AA0E0",
        "blue": "#2F5FA8", "navy": "#243A6B", "violet": "#7B4BA8", "purple": "#6A3A8A", "magenta": "#C040A8", "pink": "#D9739F",
        "brown": "#7A5B40", "tan": "#C8A878", "grey": "#8C9096", "white": "#F1EFE9",
    }
    Es = {k: irradiance(p) for k, p in PRESETS.items()}
    best = {"Day": [], "Night": []}
    for (na, ha), (nb, hb) in itertools.combinations(cand.items(), 2):
        for ea, eb in ((0.0, 0.0), (1.5, 0.0), (0.0, 1.5), (1.5, 1.5)):
            st = {k: pair_stats((ha, ea), (hb, eb), E) for k, E in Es.items()}
            fd, fn = confusable(*st["Day"]), confusable(*st["Night"])
            if fd and not fn:
                margin = min(st["Day"][0] - 10, 15 - st["Day"][1], max(st["Night"][1] - 15, 10 - st["Night"][0]))
                best["Day"].append((margin, na, ea, nb, eb, st))
            if fn and not fd:
                margin = min(st["Night"][0] - 10, 15 - st["Night"][1], max(st["Day"][1] - 15, 10 - st["Day"][0]))
                best["Night"].append((margin, na, ea, nb, eb, st))
    for k in best:
        print(f"\nTop one-sided pairs: confusable in {k} only")
        for m, na, ea, nb, eb, st in sorted(best[k], key=lambda r: -r[0])[:12]:
            print(f"  margin {m:5.1f}  {na}(em {ea}) / {nb}(em {eb})   " +
                  "  ".join(f"{p}: {st[p][0]:.1f}/{st[p][1]:.1f}" for p in PRESETS))


if __name__ == "__main__":
    for k, p in PRESETS.items():
        print(k, "to-light:", np.round(-rot_forward(p["pitch"], p["yaw"]), 4).tolist())
    if "--search" in sys.argv:
        search()
    report(SCENE, "NightCity scene colours")
