"""Generate a terrain heightmap from quantum interference and bake it for Unity.

The register holds two coordinates: k qubits for x and k qubits for y, so a
2^k x 2^k map needs 2k qubits. The circuit has three stages:

1. Load a fractal spectrum: amplitude falls off with spatial frequency, which
   is what gives terrain large landforms with fine detail on top.
2. Apply a diagonal phase layer. RZ alone leaves the map a grid of axis-aligned
   lines, and the controlled-phase gates are what dissolve it into landforms.
3. Apply a QFT to each axis. Every frequency interferes, and the pattern of
   constructive and destructive interference becomes the terrain.

Height comes from the measurement probability, either exactly (statevector) or
estimated from a finite number of shots.
"""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from qiskit import QuantumCircuit, transpile
from qiskit.circuit.library import QFTGate
from qiskit_aer import AerSimulator


def fractal_spectrum(k, beta):
    """Return normalized amplitudes for a 2^k x 2^k isotropic 1/f^beta spectrum."""
    freq = np.fft.fftfreq(2**k) * 2**k
    fy, fx = np.meshgrid(freq, freq, indexing="ij")
    amplitudes = (1.0 + np.hypot(fx, fy)) ** (-beta / 2.0)
    return (amplitudes / np.linalg.norm(amplitudes)).astype(complex)


def phase_layer(k, seed, entangle):
    """Build the diagonal phase circuit. `entangle` scales the controlled phases."""
    n = 2 * k
    rng = np.random.default_rng(seed)
    circuit = QuantumCircuit(n, name="phases")
    for q in range(n):
        circuit.rz(rng.uniform(0, 2 * np.pi), q)
    for a in range(n):
        for b in range(a + 1, n):
            circuit.cp(entangle * rng.uniform(0, 2 * np.pi), a, b)
    return circuit


def build_circuit(k, beta, seed, entangle):
    """Assemble the full circuit: spectrum, phases, then a QFT on each axis."""
    n = 2 * k
    circuit = QuantumCircuit(n)
    circuit.set_statevector(fractal_spectrum(k, beta).ravel())
    circuit.compose(phase_layer(k, seed, entangle), inplace=True)
    circuit.append(QFTGate(k), range(k))
    circuit.append(QFTGate(k), range(k, n))
    return circuit


def simulate(circuit, shots=None, seed=0):
    """Run on Aer and return (probabilities, statevector). Shots may be None."""
    backend = AerSimulator(method="statevector")
    exact = circuit.copy()
    exact.save_statevector()
    # Level 2 and above elide the QFT swaps, which silently permutes the qubits
    # of a saved statevector. Level 1 keeps the qubit order intact.
    result = backend.run(transpile(exact, backend, optimization_level=1)).result()
    state = np.asarray(result.get_statevector())
    if shots is None:
        return np.abs(state) ** 2, state

    sampled = circuit.copy()
    sampled.measure_all()
    counts = (
        backend.run(transpile(sampled, backend, optimization_level=1), shots=shots, seed_simulator=seed)
        .result()
        .get_counts()
    )
    probabilities = np.zeros(2**circuit.num_qubits)
    for bits, count in counts.items():
        probabilities[int(bits, 2)] = count / shots
    return probabilities, state


def entanglement_entropy(state, k):
    """Return the x/y entanglement entropy in bits, from the Schmidt spectrum."""
    schmidt = np.linalg.svd(state.reshape(2**k, 2**k), compute_uv=False)
    weights = schmidt**2
    weights = weights[weights > 1e-15]
    return float(-(weights * np.log2(weights)).sum())


def to_heightmap(probabilities, k, gamma):
    """Compress probabilities with gamma and normalize heights to [0, 1]."""
    side = 2**k
    height = probabilities.reshape(side, side) ** gamma
    low, high = np.percentile(height, [0.5, 99.5])
    return np.clip((height - low) / (high - low), 0.0, 1.0)


def bake(height, out_dir, name, metadata):
    """Write the 16-bit little-endian heightmap, a PNG preview and metadata."""
    out_dir.mkdir(parents=True, exist_ok=True)
    quantized = np.round(height * 65535).astype("<u2")
    (out_dir / f"{name}.r16").write_bytes(quantized.tobytes())
    Image.fromarray(np.round(height * 255).astype(np.uint8)).save(out_dir / f"{name}.png")
    (out_dir / f"{name}.json").write_text(json.dumps(metadata, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--qubits-per-axis", type=int, default=8)
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--beta", type=float, default=5.5, help="fractal falloff; higher is smoother")
    parser.add_argument("--entangle", type=float, default=1.0, help="controlled-phase strength, 0 to 1")
    parser.add_argument("--gamma", type=float, default=0.5, help="height = probability^gamma")
    parser.add_argument("--shots", type=int, default=None, help="sample instead of exact probabilities")
    parser.add_argument("--name", default="terrain")
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parent.parent / "baked")
    args = parser.parse_args()

    k = args.qubits_per_axis
    circuit = build_circuit(k, args.beta, args.seed, args.entangle)
    probabilities, state = simulate(circuit, args.shots, args.seed)
    height = to_heightmap(probabilities, k, args.gamma)
    entropy = entanglement_entropy(state, k)

    metadata = {
        "name": args.name,
        "resolution": 2**k,
        "qubits": 2 * k,
        "seed": args.seed,
        "beta": args.beta,
        "entangle": args.entangle,
        "gamma": args.gamma,
        "shots": args.shots,
        "entanglement_entropy_bits": round(entropy, 4),
    }
    bake(height, args.out, args.name, metadata)
    print(json.dumps(metadata, indent=2))


if __name__ == "__main__":
    main()
