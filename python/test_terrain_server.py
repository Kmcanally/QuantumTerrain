"""Exercise the terrain server over a real local connection."""

import threading
import urllib.error
import urllib.request
from http.server import HTTPServer

import pytest

import generate_terrain as g
import terrain_server as s


@pytest.fixture(scope="module")
def base_url():
    server = HTTPServer(("127.0.0.1", 0), s.TerrainHandler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    yield f"http://127.0.0.1:{server.server_address[1]}"
    server.shutdown()
    server.server_close()


def test_health(base_url):
    assert urllib.request.urlopen(f"{base_url}/health").read() == b"ok"


def test_terrain_matches_direct_generation(base_url):
    query = "qubits_per_axis=4&seed=3&beta=5&entangle=0.5&gamma=0.5"
    response = urllib.request.urlopen(f"{base_url}/terrain?{query}")
    height, entropy = g.generate(4, 5.0, 3, 0.5, 0.5)
    assert response.read() == g.to_r16(height)
    assert float(response.headers["X-Entanglement-Entropy"]) == pytest.approx(entropy, abs=1e-3)


def test_shots_request_returns_a_full_map(base_url):
    response = urllib.request.urlopen(f"{base_url}/terrain?qubits_per_axis=4&shots=1000")
    assert len(response.read()) == 2 * 16 * 16


def test_detail_parameter_reaches_the_generator(base_url):
    response = urllib.request.urlopen(f"{base_url}/terrain?qubits_per_axis=6&detail=0.2")
    height, _ = g.generate(6, 5.5, 7, 1.0, 0.5, detail=0.2)
    assert response.read() == g.to_r16(height)


@pytest.mark.parametrize("query", ["qubits_per_axis=99", "gamma=0", "shots=0", "beta=oops", "detail=2"])
def test_invalid_parameters_are_rejected(base_url, query):
    with pytest.raises(urllib.error.HTTPError) as error:
        urllib.request.urlopen(f"{base_url}/terrain?{query}")
    assert error.value.code == 400
