"""Explicit float64 reference colour maths for SemanticVR.

Everything operates on LINEAR sRGB (BT.709 primaries, D65) in [0,1], arrays of
shape (..., 3).  The formulas are written out so they port 1:1 to C# / HLSL.
colour-science is used only as a cross-check in python/tests.

Machado note
------------
Machado et al. (2009) publish matrices at severities 0.0, 0.1, ... 1.0.  For
severities in between we interpolate LINEARLY BETWEEN THE TWO BRACKETING
MATRICES.  colour-science 0.4.7 `matrix_cvd_Machado2009` instead extrapolates
from the *upper* segment for off-grid severities (e.g. 0.15 uses the 0.2->0.3
slope), which differs from bracketing interpolation by up to ~0.095 in matrix
entries.  The two agree exactly on the 0.1 grid.  See tests/test_reference.py.
"""
from __future__ import annotations

import numpy as np

CVD_TYPES = ("protan", "deutan", "tritan")

# Machado 2009 matrices (values as shipped by colour-science, CVD_MATRICES_MACHADO2010),
# indexed by severity 0.0, 0.1, ..., 1.0.  Rows act on column vectors of linear sRGB.
_MACHADO_TABLE = {
    "protan": (
        ((1.0, 0.0, -0.0), (0.0, 1.0, 0.0), (-0.0, -0.0, 1.0)),  # 0.0
        ((0.856167, 0.182038, -0.038205), (0.029342, 0.955115, 0.015544), (-0.00288, -0.001563, 1.004443)),  # 0.1
        ((0.734766, 0.334872, -0.069637), (0.05184, 0.919198, 0.028963), (-0.004928, -0.004209, 1.009137)),  # 0.2
        ((0.630323, 0.465641, -0.095964), (0.069181, 0.890046, 0.040773), (-0.006308, -0.007724, 1.014032)),  # 0.3
        ((0.539009, 0.579343, -0.118352), (0.082546, 0.866121, 0.051332), (-0.007136, -0.011959, 1.019095)),  # 0.4
        ((0.458064, 0.679578, -0.137642), (0.092785, 0.846313, 0.060902), (-0.007494, -0.016807, 1.024301)),  # 0.5
        ((0.38545, 0.769005, -0.154455), (0.100526, 0.829802, 0.069673), (-0.007442, -0.02219, 1.029632)),  # 0.6
        ((0.319627, 0.849633, -0.169261), (0.106241, 0.815969, 0.07779), (-0.007025, -0.028051, 1.035076)),  # 0.7
        ((0.259411, 0.923008, -0.18242), (0.110296, 0.80434, 0.085364), (-0.006276, -0.034346, 1.040622)),  # 0.8
        ((0.203876, 0.990338, -0.194214), (0.112975, 0.794542, 0.092483), (-0.005222, -0.041043, 1.046265)),  # 0.9
        ((0.152286, 1.052583, -0.204868), (0.114503, 0.786281, 0.099216), (-0.003882, -0.048116, 1.051998)),  # 1.0
    ),
    "deutan": (
        ((1.0, 0.0, -0.0), (0.0, 1.0, 0.0), (-0.0, -0.0, 1.0)),  # 0.0
        ((0.866435, 0.177704, -0.044139), (0.049567, 0.939063, 0.01137), (-0.003453, 0.007233, 0.99622)),  # 0.1
        ((0.760729, 0.319078, -0.079807), (0.090568, 0.889315, 0.020117), (-0.006027, 0.013325, 0.992702)),  # 0.2
        ((0.675425, 0.43385, -0.109275), (0.125303, 0.847755, 0.026942), (-0.00795, 0.018572, 0.989378)),  # 0.3
        ((0.605511, 0.52856, -0.134071), (0.155318, 0.812366, 0.032316), (-0.009376, 0.023176, 0.9862)),  # 0.4
        ((0.547494, 0.607765, -0.155259), (0.181692, 0.781742, 0.036566), (-0.01041, 0.027275, 0.983136)),  # 0.5
        ((0.498864, 0.674741, -0.173604), (0.205199, 0.754872, 0.039929), (-0.011131, 0.030969, 0.980162)),  # 0.6
        ((0.457771, 0.731899, -0.18967), (0.226409, 0.731012, 0.042579), (-0.011595, 0.034333, 0.977261)),  # 0.7
        ((0.422823, 0.781057, -0.203881), (0.245752, 0.709602, 0.044646), (-0.011843, 0.037423, 0.974421)),  # 0.8
        ((0.392952, 0.82361, -0.216562), (0.263559, 0.69021, 0.046232), (-0.01191, 0.040281, 0.97163)),  # 0.9
        ((0.367322, 0.860646, -0.227968), (0.280085, 0.672501, 0.047413), (-0.01182, 0.04294, 0.968881)),  # 1.0
    ),
    "tritan": (
        ((1.0, 0.0, -0.0), (0.0, 1.0, 0.0), (-0.0, -0.0, 1.0)),  # 0.0
        ((0.92667, 0.092514, -0.019184), (0.021191, 0.964503, 0.014306), (0.008437, 0.054813, 0.93675)),  # 0.1
        ((0.89572, 0.13333, -0.02905), (0.029997, 0.9454, 0.024603), (0.013027, 0.104707, 0.882266)),  # 0.2
        ((0.905871, 0.127791, -0.033662), (0.026856, 0.941251, 0.031893), (0.01341, 0.148296, 0.838294)),  # 0.3
        ((0.948035, 0.08949, -0.037526), (0.014364, 0.946792, 0.038844), (0.010853, 0.193991, 0.795156)),  # 0.4
        ((1.017277, 0.027029, -0.044306), (-0.006113, 0.958479, 0.047634), (0.006379, 0.248708, 0.744913)),  # 0.5
        ((1.104996, -0.046633, -0.058363), (-0.032137, 0.971635, 0.060503), (0.001336, 0.317922, 0.680742)),  # 0.6
        ((1.193214, -0.109812, -0.083402), (-0.058496, 0.97941, 0.079086), (-0.002346, 0.403492, 0.598854)),  # 0.7
        ((1.257728, -0.139648, -0.118081), (-0.078003, 0.975409, 0.102594), (-0.003316, 0.501214, 0.502102)),  # 0.8
        ((1.278864, -0.125333, -0.153531), (-0.084748, 0.957674, 0.127074), (-0.000989, 0.601151, 0.399838)),  # 0.9
        ((1.255528, -0.076749, -0.178779), (-0.078411, 0.930809, 0.147602), (0.004733, 0.691367, 0.3039)),  # 1.0
    ),
}
_MACHADO = {k: np.array(v, dtype=np.float64) for k, v in _MACHADO_TABLE.items()}  # (11,3,3)

# sRGB (IEC 61966-2-1, 4-digit matrix as used by colour-science) linear RGB -> XYZ, D65.
RGB_TO_XYZ = np.array([
    [0.4124, 0.3576, 0.1805],
    [0.2126, 0.7152, 0.0722],
    [0.0193, 0.1192, 0.9505],
], dtype=np.float64)

# D65 white from CIE 1931 xy = (0.3127, 0.3290), Y = 1.
WHITE_D65 = np.array([0.3127 / 0.3290, 1.0, (1.0 - 0.3127 - 0.3290) / 0.3290], dtype=np.float64)

# CIE Lab constants (exact rationals)
LAB_EPSILON = 216.0 / 24389.0
LAB_KAPPA = 24389.0 / 27.0


def machado_matrix(cvd_type: str, severity: float) -> np.ndarray:
    """3x3 Machado 2009 matrix for linear sRGB; severity in [0,1], bracketing linear interpolation."""
    if cvd_type not in _MACHADO:
        raise ValueError(f"cvd_type must be one of {CVD_TYPES}, got {cvd_type!r}")
    s = float(np.clip(severity, 0.0, 1.0))
    x = s * 10.0
    i = min(int(np.floor(x)), 9)   # lower table index, 0..9
    t = x - i                      # fraction within segment
    m = _MACHADO[cvd_type]
    return m[i] + t * (m[i + 1] - m[i])


def simulate_cvd(rgb_lin, cvd_type: str, severity: float) -> np.ndarray:
    """Simulated linear sRGB (not clamped; the Machado matrices can leave [0,1] slightly)."""
    rgb = np.asarray(rgb_lin, dtype=np.float64)
    return rgb @ machado_matrix(cvd_type, severity).T


def linear_rgb_to_xyz(rgb_lin) -> np.ndarray:
    return np.asarray(rgb_lin, dtype=np.float64) @ RGB_TO_XYZ.T


def _lab_f(t: np.ndarray) -> np.ndarray:
    return np.where(t > LAB_EPSILON, np.cbrt(t), (LAB_KAPPA * t + 16.0) / 116.0)


def xyz_to_lab(xyz, white=WHITE_D65) -> np.ndarray:
    xyz = np.asarray(xyz, dtype=np.float64)
    f = _lab_f(xyz / white)
    L = 116.0 * f[..., 1] - 16.0
    a = 500.0 * (f[..., 0] - f[..., 1])
    b = 200.0 * (f[..., 1] - f[..., 2])
    return np.stack([L, a, b], axis=-1)


def rgb_lin_to_lab(rgb_lin) -> np.ndarray:
    return xyz_to_lab(linear_rgb_to_xyz(rgb_lin))


def ciede2000(lab1, lab2) -> np.ndarray:
    """CIEDE2000 colour difference, kL = kC = kH = 1 (Sharma, Wu, Dalal 2005)."""
    lab1 = np.asarray(lab1, dtype=np.float64)
    lab2 = np.asarray(lab2, dtype=np.float64)
    L1, a1, b1 = lab1[..., 0], lab1[..., 1], lab1[..., 2]
    L2, a2, b2 = lab2[..., 0], lab2[..., 1], lab2[..., 2]

    C1 = np.hypot(a1, b1)
    C2 = np.hypot(a2, b2)
    Cbar = 0.5 * (C1 + C2)
    Cbar7 = Cbar ** 7
    G = 0.5 * (1.0 - np.sqrt(Cbar7 / (Cbar7 + 25.0 ** 7)))
    a1p = (1.0 + G) * a1
    a2p = (1.0 + G) * a2
    C1p = np.hypot(a1p, b1)
    C2p = np.hypot(a2p, b2)
    # hue angles in degrees [0,360); atan2(0,0) -> 0
    h1p = np.degrees(np.arctan2(b1, a1p)) % 360.0
    h2p = np.degrees(np.arctan2(b2, a2p)) % 360.0

    dLp = L2 - L1
    dCp = C2p - C1p
    chroma_zero = (C1p * C2p) == 0.0
    dh = h2p - h1p
    dh = np.where(dh > 180.0, dh - 360.0, dh)
    dh = np.where(dh < -180.0, dh + 360.0, dh)
    dhp = np.where(chroma_zero, 0.0, dh)
    dHp = 2.0 * np.sqrt(C1p * C2p) * np.sin(np.radians(dhp / 2.0))

    Lbarp = 0.5 * (L1 + L2)
    Cbarp = 0.5 * (C1p + C2p)
    hsum = h1p + h2p
    hdiff = np.abs(h1p - h2p)
    hbar = np.where(
        chroma_zero, hsum,
        np.where(hdiff <= 180.0, 0.5 * hsum,
                 np.where(hsum < 360.0, 0.5 * (hsum + 360.0), 0.5 * (hsum - 360.0))))

    T = (1.0
         - 0.17 * np.cos(np.radians(hbar - 30.0))
         + 0.24 * np.cos(np.radians(2.0 * hbar))
         + 0.32 * np.cos(np.radians(3.0 * hbar + 6.0))
         - 0.20 * np.cos(np.radians(4.0 * hbar - 63.0)))
    dtheta = 30.0 * np.exp(-(((hbar - 275.0) / 25.0) ** 2))
    Cbarp7 = Cbarp ** 7
    Rc = 2.0 * np.sqrt(Cbarp7 / (Cbarp7 + 25.0 ** 7))
    Lm50sq = (Lbarp - 50.0) ** 2
    Sl = 1.0 + 0.015 * Lm50sq / np.sqrt(20.0 + Lm50sq)
    Sc = 1.0 + 0.045 * Cbarp
    Sh = 1.0 + 0.015 * Cbarp * T
    Rt = -np.sin(np.radians(2.0 * dtheta)) * Rc

    tl = dLp / Sl
    tc = dCp / Sc
    th = dHp / Sh
    return np.sqrt(tl * tl + tc * tc + th * th + Rt * tc * th)
