"""Compare quantum terrains with classical generators: speed, statistics, and whether they can be told apart.

Generators:
- quantum: the circuit in generate_terrain.py
- random phase: the same spectrum and height processing, with independent random phases
  instead of the circuit's phases. This isolates what the quantum phase structure adds.
- fbm: a standard real Gaussian fractal field with the same spectrum, the usual
  classical way to make terrain.

A classifier is trained on terrain statistics with 5-fold cross-validation. About 50%
accuracy means the sets can't be told apart; near 100% means they clearly differ.
"""

import argparse
import time

import numpy as np

import generate_terrain as g

K = 8
BETA = 5.5
GAMMA = 0.5
HEIGHT_SCALE = 200
CELL = 1000 / 2**K

FEATURES = [
    "mean slope (deg)",
    "90th pct slope (deg)",
    "height skewness",
    "height kurtosis",
    "mirror symmetry",
    "x/y separability (bits)",
    "axis-aligned power",
    "spectral slope",
]


def normalize(height):
    low, high = np.percentile(height, [0.5, 99.5])
    return np.clip((height - low) / (high - low), 0.0, 1.0)


def quantum(seed, entangle):
    return g.generate(K, BETA, seed, entangle, GAMMA)[0]


def complex_random_field(seed):
    rng = np.random.default_rng(10_000 + seed)
    spectrum = g.fractal_spectrum(K, BETA) * np.exp(1j * rng.uniform(0, 2 * np.pi, (2**K, 2**K)))
    return np.fft.ifft2(spectrum, norm="ortho")


def random_phase(seed):
    return g.to_heightmap(np.abs(complex_random_field(seed).ravel()) ** 2, K, GAMMA)


def fbm(seed):
    return normalize(complex_random_field(seed).real)


def mirror_symmetry(h):
    """Best correlation between the map and any wrap-around flip of itself."""
    a = (h - h.mean()) / h.std()
    best = -1.0
    for flipped in (a[:, ::-1], a[::-1, :], a[::-1, ::-1]):
        corr = np.fft.ifft2(np.fft.fft2(a) * np.conj(np.fft.fft2(flipped))).real / a.size
        best = max(best, corr.max())
    return best


def features(h):
    gy, gx = np.gradient(h * HEIGHT_SCALE, CELL)
    slope = np.degrees(np.arctan(np.hypot(gx, gy)))
    z = (h - h.mean()) / h.std()

    singular = np.linalg.svd(h - h.mean(), compute_uv=False) ** 2
    weights = singular / singular.sum()
    weights = weights[weights > 1e-15]

    power = np.abs(np.fft.fft2(h - h.mean())) ** 2
    power[0, 0] = 0.0
    axis_power = (power[0, :].sum() + power[:, 0].sum()) / power.sum()

    freq = np.fft.fftfreq(2**K) * 2**K
    fy, fx = np.meshgrid(freq, freq, indexing="ij")
    radius = np.hypot(fx, fy).ravel()
    band = (radius >= 2) & (radius <= 64)
    spectral_slope = np.polyfit(np.log(radius[band]), np.log(power.ravel()[band] + 1e-30), 1)[0]

    return np.array([
        slope.mean(),
        np.percentile(slope, 90),
        (z**3).mean(),
        (z**4).mean(),
        mirror_symmetry(h),
        float(-(weights * np.log2(weights)).sum()),
        axis_power,
        spectral_slope,
    ])


def cross_validated_accuracy(a, b, folds=5, seed=0):
    """Linear discriminant accuracy separating feature sets a and b."""
    x = np.vstack([a, b])
    y = np.r_[np.zeros(len(a)), np.ones(len(b))]
    order = np.random.default_rng(seed).permutation(len(y))
    x, y = x[order], y[order]
    correct = 0
    for fold in np.array_split(np.arange(len(y)), folds):
        train = np.setdiff1d(np.arange(len(y)), fold)
        mean, std = x[train].mean(0), x[train].std(0) + 1e-12
        xt, xf = (x[train] - mean) / std, (x[fold] - mean) / std
        m0, m1 = xt[y[train] == 0].mean(0), xt[y[train] == 1].mean(0)
        within = np.cov(xt[y[train] == 0].T) + np.cov(xt[y[train] == 1].T)
        w = np.linalg.solve(within + 1e-6 * np.eye(len(m0)), m1 - m0)
        threshold = w @ (m0 + m1) / 2
        correct += ((xf @ w > threshold) == y[fold]).sum()
    return correct / len(y)


def effect_size(a, b):
    """Cohen's d per feature: difference in means over pooled spread."""
    return (a.mean(0) - b.mean(0)) / np.sqrt((a.var(0) + b.var(0)) / 2 + 1e-24)


def collect(name, make, seeds):
    start = time.perf_counter()
    maps = [make(s) for s in seeds]
    per_map = (time.perf_counter() - start) / len(seeds)
    print(f"{name:<22} {per_map * 1000:8.1f} ms per terrain")
    return np.array([features(h) for h in maps]), maps


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--count", type=int, default=200)
    args = parser.parse_args()
    seeds = range(args.count)

    print(f"--- speed ({2**K}x{2**K}, {args.count} terrains each) ---")
    sets = {
        "quantum": collect("quantum (entangle 1)", lambda s: quantum(s, 1.0), seeds),
        "quantum e0": collect("quantum (entangle 0)", lambda s: quantum(s, 0.0), seeds),
        "random phase": collect("random phase", random_phase, seeds),
        "random phase B": collect("random phase (batch 2)", lambda s: random_phase(s + 5000), seeds),
        "fbm": collect("fbm", fbm, seeds),
    }

    print("\n--- can a classifier tell them apart? (50% = no, 100% = always) ---")
    comparisons = [
        ("quantum", "random phase", "main result"),
        ("quantum", "fbm", "vs standard terrain noise"),
        ("quantum e0", "random phase", "positive control: should be high"),
        ("random phase", "random phase B", "negative control: should be ~50%"),
    ]
    for a, b, note in comparisons:
        accuracy = cross_validated_accuracy(sets[a][0], sets[b][0])
        print(f"{a:>12} vs {b:<15} {accuracy * 100:5.1f}%   ({note})")

    print("\n--- per-feature means, and effect size of quantum vs random phase ---")
    d = effect_size(sets["quantum"][0], sets["random phase"][0])
    print(f"{'feature':<26}{'quantum':>10}{'q. e=0':>10}{'rand ph':>10}{'fbm':>10}{'d (q-rp)':>10}")
    for i, name in enumerate(FEATURES):
        row = [sets[key][0][:, i].mean() for key in ("quantum", "quantum e0", "random phase", "fbm")]
        print(f"{name:<26}" + "".join(f"{v:10.3f}" for v in row) + f"{d[i]:10.2f}")
    return sets


if __name__ == "__main__":
    main()
