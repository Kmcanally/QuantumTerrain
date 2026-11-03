"""Check the simulated circuit against an independent NumPy calculation."""

import numpy as np

import generate_terrain as g

K, BETA, SEED = 5, 3.0, 7


def numpy_reference(entangle):
    """Compute the output probabilities without going through Qiskit."""
    n = 2 * K
    size = 2**n
    index = np.arange(size)
    x, y = index & (2**K - 1), index >> K
    x = np.where(x >> (K - 1), x ^ (2 ** (K - 1) - 1), x)
    y = np.where(y >> (K - 1), y ^ (2 ** (K - 1) - 1), y)
    bits = (((y << K) | x)[:, None] >> np.arange(n)) & 1
    single, pairs = g.phase_angles(K, SEED)
    phase = np.zeros(size)
    for q in range(n):
        phase += single[q] * (2 * bits[:, q] - 1) / 2
    for a in range(n):
        for b in range(a + 1, n):
            phase += entangle * pairs[a, b] * bits[:, a] * bits[:, b]
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


def test_small_entangle_steps_barely_move_the_terrain():
    first, _ = g.generate(8, 5.5, SEED, 0.5, 0.5)
    second, _ = g.generate(8, 5.5, SEED, 0.525, 0.5)
    assert np.abs(first - second).mean() < 0.06


def test_a_seed_keeps_its_terrain_at_higher_resolution():
    coarse, _ = g.generate(7, 5.5, SEED, 1.0, 0.5)
    fine, _ = g.generate(8, 5.5, SEED, 1.0, 0.5)
    downsampled = fine.reshape(128, 2, 128, 2).mean(axis=(1, 3))
    assert np.corrcoef(coarse.ravel(), downsampled.ravel())[0, 1] > 0.99


def test_detail_roughens_small_scales_but_keeps_landforms():
    smooth, _ = g.generate(8, 5.5, SEED, 1.0, 0.5, detail=0.0)
    detailed, _ = g.generate(8, 5.5, SEED, 1.0, 0.5, detail=1.0)

    def landforms(h):
        return h.reshape(32, 8, 32, 8).mean(axis=(1, 3)).ravel()

    def roughness(h):
        return np.abs(np.diff(h, axis=1)).mean()

    assert np.corrcoef(landforms(smooth), landforms(detailed))[0, 1] > 0.98
    assert roughness(detailed) > 1.1 * roughness(smooth)


def test_entropy_of_product_state_is_zero():
    state = np.kron(np.array([0.6, 0.8]), np.array([0.8, 0.6]))
    assert abs(g.entanglement_entropy(state, 1)) < 1e-9


def test_entropy_of_bell_state_is_one_bit():
    state = np.array([1, 0, 0, 1]) / np.sqrt(2)
    assert abs(g.entanglement_entropy(state, 1) - 1.0) < 1e-9
