"""Serve quantum heightmaps to the Unity editor over a local HTTP connection.

GET /terrain returns the raw 16-bit heightmap for the query parameters
qubits_per_axis, seed, beta, entangle, gamma, shots and detail. The entanglement
entropy and generation time come back in response headers.
"""

import argparse
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

import generate_terrain as g

DEFAULT_PORT = 8765


def parse_terrain_query(query):
    """Turn a query string into generate() arguments, or raise ValueError."""
    values = parse_qs(query)

    def read(name, convert, default):
        return convert(values[name][0]) if name in values else default

    k = read("qubits_per_axis", int, 8)
    beta = read("beta", float, 5.5)
    entangle = read("entangle", float, 1.0)
    gamma = read("gamma", float, 0.5)
    shots = read("shots", int, None)
    detail = read("detail", float, g.DEFAULT_DETAIL)
    if not 0 <= detail <= 1:
        raise ValueError("detail must be between 0 and 1")
    if not 2 <= k <= 10:
        raise ValueError("qubits_per_axis must be between 2 and 10")
    if beta < 0 or gamma <= 0:
        raise ValueError("beta must not be negative and gamma must be positive")
    if shots is not None and shots < 1:
        raise ValueError("shots must be positive")
    return k, beta, read("seed", int, 7), entangle, gamma, shots, detail


class TerrainHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/health":
            self.reply(200, b"ok", "text/plain")
        elif url.path == "/terrain":
            self.serve_terrain(url.query)
        else:
            self.reply(404, b"not found", "text/plain")

    def serve_terrain(self, query):
        try:
            args = parse_terrain_query(query)
        except ValueError as error:
            self.reply(400, str(error).encode(), "text/plain")
            return
        start = time.perf_counter()
        height, entropy = g.generate(*args)
        headers = {
            "X-Entanglement-Entropy": f"{entropy:.4f}",
            "X-Generation-Ms": f"{(time.perf_counter() - start) * 1000:.0f}",
        }
        self.reply(200, g.to_r16(height), "application/octet-stream", headers)

    def reply(self, status, body, content_type, headers=None):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        for name, value in (headers or {}).items():
            self.send_header(name, value)
        self.end_headers()
        self.wfile.write(body)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    args = parser.parse_args()
    server = HTTPServer(("127.0.0.1", args.port), TerrainHandler)
    print(f"Quantum terrain server on http://127.0.0.1:{args.port} (Ctrl+C to stop)")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
