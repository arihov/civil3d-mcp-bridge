"""Pure-Python highway geometry and survey calculations (no Civil 3D needed).

Metric AASHTO "A Policy on Geometric Design of Highways and Streets" (Green Book,
7th ed. 2018) relationships. Project criteria (e.g. the MoWT Road Design Manual,
Vol. 1 Geometric Design) govern where they differ - every result says which
basis it used so a reviewer can trace it.
"""

from __future__ import annotations

import math
from typing import Any

BASIS = "AASHTO Green Book 2018 (metric)"

# Side-friction factor f_max for open-road curves, AASHTO 2018 Table 3-7 (metric).
F_MAX = {15: 0.40, 20: 0.35, 30: 0.28, 40: 0.23, 50: 0.19, 60: 0.17, 70: 0.15,
         80: 0.14, 90: 0.13, 100: 0.12, 110: 0.11, 120: 0.09, 130: 0.08}

# Maximum relative gradient for superelevation runoff, AASHTO 2018 Table 3-15 (metric), %.
DELTA_MAX = {20: 0.80, 30: 0.75, 40: 0.70, 50: 0.65, 60: 0.60, 70: 0.55, 80: 0.50,
             90: 0.47, 100: 0.44, 110: 0.41, 120: 0.38, 130: 0.35}

# Adjustment factor b_w for number of lanes rotated, AASHTO 2018 Table 3-16.
BW = {1.0: 1.00, 1.5: 0.83, 2.0: 0.75, 2.5: 0.70, 3.0: 0.67, 3.5: 0.64}


def _interp(table: dict[float, float], x: float) -> float:
    keys = sorted(table)
    if x <= keys[0]:
        return table[keys[0]]
    if x >= keys[-1]:
        return table[keys[-1]]
    for a, b in zip(keys, keys[1:]):
        if a <= x <= b:
            return table[a] + (table[b] - table[a]) * (x - a) / (b - a)
    return table[keys[-1]]


def stopping_sight_distance(v_kph: float, grade_pct: float = 0.0, t_s: float = 2.5, a: float = 3.4) -> float:
    """SSD = 0.278 V t + V^2 / (254 (a/9.81 +/- G)). Level road reduces to 0.039 V^2 / a."""
    g = grade_pct / 100.0
    braking = v_kph ** 2 / (254.0 * ((a / 9.81) + g))
    return 0.278 * v_kph * t_s + braking


def k_crest(ssd: float) -> float:
    """Crest K for S < L, eye 1.08 m, object 0.60 m: K = S^2 / 658."""
    return ssd ** 2 / 658.0


def k_sag(ssd: float) -> float:
    """Sag K for headlight sight distance, S < L: K = S^2 / (120 + 3.5 S)."""
    return ssd ** 2 / (120.0 + 3.5 * ssd)


def min_radius(v_kph: float, e_max_pct: float) -> tuple[float, float]:
    """R_min = V^2 / (127 (e_max + f_max)). Returns (R_min, f_max)."""
    f = _interp(F_MAX, v_kph)
    return v_kph ** 2 / (127.0 * (e_max_pct / 100.0 + f)), f


def design_controls(v_kph: float, grade_pct: float = 0.0, t_s: float = 2.5, e_max_pct: float = 8.0) -> dict[str, Any]:
    ssd = stopping_sight_distance(v_kph, grade_pct, t_s)
    ssd_level = stopping_sight_distance(v_kph, 0.0, t_s)
    rmin, f = min_radius(v_kph, e_max_pct)
    return {
        "basis": BASIS,
        "designSpeedKph": v_kph,
        "stoppingSightDistance_m": round(ssd, 1),
        "stoppingSightDistanceLevel_m": round(ssd_level, 1),
        "kCrestMin": round(k_crest(ssd_level), 1),
        "kSagMin": round(k_sag(ssd_level), 1),
        "minVerticalCurveLength_m": round(max(0.6 * v_kph, 0), 1),
        "minRadius_m": round(rmin, 1),
        "fMax": round(f, 3),
        "eMaxPct": e_max_pct,
        "assumptions": [
            f"perception-reaction {t_s} s, deceleration 3.4 m/s2",
            "K values use level-road SSD with S < L; h1 = 1.08 m, h2 = 0.60 m (crest); headlight 0.60 m, 1 deg beam (sag)",
            "minimum vertical curve length taken as 0.6 V (AASHTO 2018 guidance for appearance)",
        ],
    }


# ─── survey / COGO ────────────────────────────────────────────────────────

def inverse(p: list[float], q: list[float]) -> dict[str, Any]:
    de, dn = q[0] - p[0], q[1] - p[1]
    dist = math.hypot(de, dn)
    brg = math.degrees(math.atan2(de, dn)) % 360.0
    return {"distance": round(dist, 4), "bearingDeg": round(brg, 6), "bearingDMS": dms(brg),
            "dE": round(de, 4), "dN": round(dn, 4)}


def forward(p: list[float], bearing_deg: float, distance: float) -> dict[str, Any]:
    b = math.radians(bearing_deg)
    return {"x": round(p[0] + distance * math.sin(b), 4), "y": round(p[1] + distance * math.cos(b), 4)}


def dms(deg: float) -> str:
    d = int(deg)
    m_full = (deg - d) * 60
    m = int(m_full)
    s = (m_full - m) * 60
    if s >= 59.995:
        s, m = 0.0, m + 1
    if m >= 60:
        m, d = 0, d + 1
    return f"{d:03d}°{m:02d}'{s:05.2f}\""


def solve_curve(radius: float, delta_deg: float, pi_station: float | None = None) -> dict[str, Any]:
    d = math.radians(delta_deg)
    t = radius * math.tan(d / 2)
    length = radius * d
    res = {
        "radius": radius, "deflectionDeg": delta_deg,
        "tangentLength": round(t, 4), "curveLength": round(length, 4),
        "longChord": round(2 * radius * math.sin(d / 2), 4),
        "externalDistance": round(radius * (1 / math.cos(d / 2) - 1), 4),
        "middleOrdinate": round(radius * (1 - math.cos(d / 2)), 4),
        "degreeOfCurve20m": round(math.degrees(2 * math.asin(10 / radius)), 4) if radius > 10 else None,
    }
    if pi_station is not None:
        res["tcStation"] = round(pi_station - t, 3)
        res["ctStation"] = round(pi_station - t + length, 3)
    return res


def bowditch(start: list[float], legs: list[tuple[float, float]], close: list[float] | None) -> dict[str, Any]:
    target = close or start
    pts = [(start[0], start[1])]
    total = 0.0
    for brg, dist in legs:
        b = math.radians(brg)
        x, y = pts[-1]
        pts.append((x + dist * math.sin(b), y + dist * math.cos(b)))
        total += dist
    mis_e = pts[-1][0] - target[0]
    mis_n = pts[-1][1] - target[1]
    linear = math.hypot(mis_e, mis_n)
    adjusted, run = [list(map(lambda v: round(v, 4), pts[0]))], 0.0
    for i, (_, dist) in enumerate(legs, start=1):
        run += dist
        k = run / total
        adjusted.append([round(pts[i][0] - mis_e * k, 4), round(pts[i][1] - mis_n * k, 4)])
    return {
        "method": "Bowditch (compass rule)",
        "perimeter": round(total, 4),
        "misclosureE": round(mis_e, 4), "misclosureN": round(mis_n, 4),
        "linearMisclosure": round(linear, 4),
        "precision": f"1:{int(total / linear):,}" if linear > 1e-9 else "exact",
        "unadjusted": [[round(x, 4), round(y, 4)] for x, y in pts],
        "adjusted": adjusted,
    }


def superelevation_runoff(v_kph: float, e_d_pct: float, w: float, n1: float, e_nc_pct: float) -> dict[str, Any]:
    delta = _interp(DELTA_MAX, v_kph)
    bw = _interp(BW, n1)
    lr = (w * n1 * e_d_pct) * bw / delta
    lt = e_nc_pct / e_d_pct * lr
    return {
        "basis": BASIS + " eq. 3-23 / 3-24",
        "maxRelativeGradientPct": round(delta, 3), "bw": round(bw, 3),
        "runoffLength_m": round(lr, 1), "tangentRunout_m": round(lt, 1),
        "totalTransition_m": round(lr + lt, 1),
        "placementNote": "AASHTO: 60-80% of runoff on the tangent for curves without spirals (67% typical)",
    }


def average_end_area(rows: list[tuple[float, float, float]], fill_factor: float = 1.0) -> dict[str, Any]:
    rows = sorted(rows)
    out, cc, cf, mass = [], 0.0, 0.0, 0.0
    for i, (st, ca, fa) in enumerate(rows):
        vc = vf = 0.0
        if i:
            d = st - rows[i - 1][0]
            vc = d * (ca + rows[i - 1][1]) / 2
            vf = d * (fa + rows[i - 1][2]) / 2
        cc += vc
        cf += vf
        mass += vc - vf * fill_factor
        out.append({"station": st, "cutVolume": round(vc, 2), "fillVolume": round(vf, 2),
                    "cumCut": round(cc, 2), "cumFill": round(cf, 2), "massOrdinate": round(mass, 2)})
    return {"method": "average end area", "fillFactor": fill_factor,
            "totalCut": round(cc, 2), "totalFill": round(cf, 2),
            "net": round(cc - cf * fill_factor, 2), "rows": out}
