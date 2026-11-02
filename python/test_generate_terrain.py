"""Check the simulated circuit against an independent NumPy calculation."""

import numpy as np

import generate_terrain as g

K, BETA, SEED = 5, 3.0, 7


def numpy_reference(entangle):
    """Compute the output probabilities without going through Qiskit."""
    n = 2 * K
    size = 2**n
    bits = (np.arange(size)[:, None] >> np.arange(n)) & 1
    rng = np.random.default_rng(SEED)
    phase = np.zeros(size)
    for q in range(n):
        phase += rng.uniform(0, 2 * np.pi) * (2 * bits[:, q] - 1) / 2
    for a in range(n):
        for b in range(a + 1, n):
            phase += entangle * rng.uniform(0, 2 * np.pi) * bits[:, a] * bits[:, b]
    spectrum = g.fractal_spectrum(K, BETA).ravel() * np.exp(1j * phase)
    return np.fft.ifft2(spectrum.reshape(2**K, 2**K), norm="ortho").ravel()


def total_variation(p, q):
    return 0.5 * np.abs(p - q).sum()


def test_statevector_matches_numpy():
    probabilities, _ = g.simulate(g.build_circuit(K, BETA, SEED, 1.0))
    assert total_variation(probabilities, np.abs(numpy_reference(1.0)) ** 2) < 1e-9


def test_shots_converge_to_statevector():
    circuit = g.build_circuit(K, BETA, SEED, 1.0)
    sampled, state = g.simulate(circuit, shots=2_000_000, seed=1)
    assert total_variation(sampled, np.abs(state) ** 2) < 0.02


def test_entropy_of_product_state_is_zero():
    state = np.kron(np.array([0.6, 0.8]), np.array([0.8, 0.6]))
    assert abs(g.entanglement_entropy(state, 1)) < 1e-9


def test_entropy_of_bell_state_is_one_bit():
    state = np.array([1, 0, 0, 1]) / np.sqrt(2)
    assert abs(g.entanglement_entropy(state, 1) - 1.0) < 1e-9
