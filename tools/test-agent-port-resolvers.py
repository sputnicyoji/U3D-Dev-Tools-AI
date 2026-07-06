import importlib.util
import json
import os
import tempfile
import threading
import time
import unittest
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Iterator
from unittest.mock import patch
from urllib.error import URLError


ROOT = Path(__file__).resolve().parents[1]
CLOSED_PORT = 1
RESOLVER_PATHS = [
    ROOT / "Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/port_resolver.py",
    ROOT / "Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/port_resolver.py",
    ROOT / "Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/port_resolver.py",
]


def load_resolver(path: Path, index: int = 0) -> Any:
    spec = importlib.util.spec_from_file_location(f"agent_port_resolver_{index}", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class _PingHandler(BaseHTTPRequestHandler):
    service_id = ""
    delay = 0.0

    def _send(self) -> None:
        if self.delay > 0:
            time.sleep(self.delay)
        body = json.dumps({"serviceId": self.service_id, "ok": True}).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionAbortedError, ConnectionResetError):
            return

    def do_GET(self) -> None:
        if self.path != "/ping":
            self.send_error(404)
            return
        self._send()

    def do_POST(self) -> None:
        if self.path != "/ping":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)
        self._send()

    def log_message(self, format: str, *args: Any) -> None:
        return


class _Server(ThreadingHTTPServer):
    allow_reuse_address = True


@contextmanager
def ping_server(service_id: str, delay: float = 0.0) -> Iterator[int]:
    handler = type("Handler", (_PingHandler,), {"service_id": service_id, "delay": delay})
    server = _Server(("127.0.0.1", 0), handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield server.server_address[1]
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


def write_json(path: Path, payload: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload), encoding="utf-8")


def make_project(root: Path) -> Path:
    (root / "ProjectSettings").mkdir(parents=True, exist_ok=True)
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f2c1", encoding="utf-8")
    return root


def registry_path(local_appdata: Path) -> Path:
    return local_appdata / "Yoji" / "U3D-Dev-Tools-AI" / "instances.json"


class PortResolverTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.project = make_project(self.root / "Project")
        self.local_appdata = self.root / "LocalAppData"
        self.env = patch.dict(os.environ, {"LOCALAPPDATA": str(self.local_appdata)})
        self.env.start()
        self.addCleanup(self.env.stop)
        self.resolver = load_resolver(RESOLVER_PATHS[0])

    def _resolve(
        self,
        *,
        port: int | None = None,
        explicit_port: int | None = None,
        project: str | None = None,
        pid: int | None = None,
        timeout: float = 1.0,
        default_port: int = 21890,
        legacy_ports: tuple[int, ...] | None = None,
    ) -> tuple[str, int, str]:
        return self.resolver.resolve_endpoint(
            "test-runner-mcp",
            "127.0.0.1",
            port,
            default_port,
            project,
            pid,
            timeout,
            legacy_ports,
            explicit_port=explicit_port,
        )

    def test_all_resolver_copies_are_byte_identical(self) -> None:
        first = RESOLVER_PATHS[0].read_bytes()
        for path in RESOLVER_PATHS[1:]:
            self.assertEqual(first, path.read_bytes(), str(path))

    def test_all_resolver_copies_import(self) -> None:
        for index, path in enumerate(RESOLVER_PATHS):
            with self.subTest(path=path):
                module = load_resolver(path, index)
                self.assertTrue(hasattr(module, "resolve_endpoint"))
                self.assertTrue(hasattr(module, "EndpointResolutionError"))

    def test_project_ports_json_resolves_single_instance(self) -> None:
        with ping_server("test-runner-mcp") as port:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {"instances": [{"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port, "processId": 101, "projectRoot": str(self.project), "instanceId": "a"}]},
            )

            host, resolved_port, source = self._resolve(project=str(self.project))

            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, port)
            self.assertEqual(source, "project-registry")

    def test_multiple_matching_records_require_pid(self) -> None:
        with ping_server("test-runner-mcp") as port1, ping_server("test-runner-mcp") as port2:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {"instances": [
                    {"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port1, "processId": 101, "projectRoot": str(self.project), "instanceId": "a"},
                    {"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port2, "processId": 202, "projectRoot": str(self.project), "instanceId": "b"},
                ]},
            )

            with self.assertRaises(SystemExit) as ctx:
                self._resolve(project=str(self.project))

            text = str(ctx.exception)
            self.assertIn("ambiguous test-runner-mcp instances", text)
            self.assertIn("pid=101", text)
            self.assertIn("pid=202", text)

    def test_pid_disambiguates(self) -> None:
        with ping_server("test-runner-mcp") as port1, ping_server("test-runner-mcp") as port2:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {"instances": [
                    {"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port1, "processId": 101, "projectRoot": str(self.project), "instanceId": "a"},
                    {"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port2, "processId": 202, "projectRoot": str(self.project), "instanceId": "b"},
                ]},
            )

            host, resolved_port, source = self._resolve(project=str(self.project), pid=202)

            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, port2)
            self.assertEqual(source, "project-registry")

    def test_duplicate_instance_rows_same_pid_port_resolve_without_ambiguity(self) -> None:
        # domain reload / 崩溃残留会给同一 pid+port 留多个 instanceId 行; 它们指向同一监听器, 不应报 ambiguous.
        with ping_server("test-runner-mcp") as port:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {"instances": [
                    {"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port, "processId": 101, "projectRoot": str(self.project), "instanceId": "old"},
                    {"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": port, "processId": 101, "projectRoot": str(self.project), "instanceId": "new"},
                ]},
            )

            host, resolved_port, source = self._resolve(project=str(self.project))

            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, port)
            self.assertEqual(source, "project-registry")

    def test_malformed_json_falls_back_to_legacy_default_unverified(self) -> None:
        ports = self.project / ".u3d-ai-linker" / "ports.json"
        ports.parent.mkdir(parents=True, exist_ok=True)
        ports.write_text("{broken", encoding="utf-8")
        default_port = CLOSED_PORT

        with patch.object(self.resolver.urllib.request, "urlopen", side_effect=URLError(ConnectionRefusedError("refused"))):
            host, resolved_port, source = self._resolve(project=str(self.project), default_port=default_port, legacy_ports=(default_port,))

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, default_port)
        self.assertEqual(source, "legacy-default-unverified")

    def test_stale_registry_candidate_is_skipped_then_legacy_healthy(self) -> None:
        with ping_server("other-service") as stale_port, ping_server("test-runner-mcp") as legacy_port:
            write_json(
                registry_path(self.local_appdata),
                {"instances": [{"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": stale_port, "processId": 333, "projectRoot": str(self.project), "instanceId": "stale"}]},
            )
            host, resolved_port, source = self._resolve(project=str(self.project), default_port=stale_port, legacy_ports=(stale_port, legacy_port))
            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, legacy_port)
            self.assertEqual(source, "legacy-healthy")

    def test_registry_connection_failure_falls_back_to_legacy_default(self) -> None:
        default_port = CLOSED_PORT
        write_json(
            registry_path(self.local_appdata),
            {"instances": [{"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": 22001, "processId": 444, "projectRoot": str(self.project), "instanceId": "dead"}]},
        )
        with patch.object(self.resolver.urllib.request, "urlopen", side_effect=URLError("boom")):
            host, resolved_port, source = self._resolve(project=str(self.project), default_port=default_port, legacy_ports=(default_port,))
            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, default_port)
            self.assertEqual(source, "legacy-default-unverified")

    def test_explicit_port_wins_without_ping(self) -> None:
        write_json(
            self.project / ".u3d-ai-linker" / "ports.json",
            {"instances": [{"serviceId": "test-runner-mcp", "host": "127.0.0.1", "port": 21900, "processId": 111, "projectRoot": str(self.project), "instanceId": "a"}]},
        )

        with patch.object(self.resolver.urllib.request, "urlopen", side_effect=AssertionError("should not ping")):
            host, resolved_port, source = self._resolve(port=29999, project=str(self.project), pid=111)

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, 29999)
        self.assertEqual(source, "explicit")

    def test_keyword_explicit_port_wins_without_ping(self) -> None:
        with patch.object(self.resolver.urllib.request, "urlopen", side_effect=AssertionError("should not ping")):
            host, resolved_port, source = self._resolve(explicit_port=29998, project=str(self.project))

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, 29998)
        self.assertEqual(source, "explicit")

    def test_keyword_explicit_port_does_not_require_default_port(self) -> None:
        with patch.object(self.resolver.urllib.request, "urlopen", side_effect=AssertionError("should not ping")):
            host, resolved_port, source = self.resolver.resolve_endpoint(
                "test-runner-mcp",
                "127.0.0.1",
                explicit_port=29997,
            )

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, 29997)
        self.assertEqual(source, "explicit")

    def test_port_and_keyword_explicit_port_must_match(self) -> None:
        with self.assertRaises(ValueError):
            self._resolve(port=29999, explicit_port=29998)

    def test_legacy_timeout_candidate_is_skipped_then_healthy_found(self) -> None:
        with ping_server("test-runner-mcp", delay=0.25) as slow_port, ping_server("test-runner-mcp") as healthy_port:
            host, resolved_port, source = self._resolve(
                default_port=slow_port,
                legacy_ports=(slow_port, healthy_port),
                timeout=0.01,
            )

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, healthy_port)
        self.assertEqual(source, "legacy-healthy")

    def test_legacy_scan_finds_healthy_non_default_candidate(self) -> None:
        with ping_server("other-service") as default_port, ping_server("test-runner-mcp") as legacy_port:
            host, resolved_port, source = self._resolve(default_port=default_port, legacy_ports=(default_port, legacy_port))

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, legacy_port)
        self.assertEqual(source, "legacy-healthy")

    def test_legacy_scan_multiple_healthy_uses_list_order(self) -> None:
        with ping_server("other-service") as default_port, ping_server("test-runner-mcp") as first, ping_server("test-runner-mcp") as second:
            host, resolved_port, source = self._resolve(default_port=default_port, legacy_ports=(default_port, first, second))

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, first)
        self.assertEqual(source, "legacy-healthy")

    def test_legacy_scan_respects_provided_ports_without_default_insertion(self) -> None:
        seen = []

        def fake_probe(service_id: str, host: str, port: int, timeout: float | None) -> dict[str, Any]:
            seen.append(port)
            if port == 45678:
                return {"status": "healthy"}
            return {"status": "connection_failed"}

        with patch.object(self.resolver, "_probe_candidate", side_effect=fake_probe):
            host, resolved_port, source = self._resolve(default_port=12345, legacy_ports=(45678,))

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, 45678)
        self.assertEqual(source, "legacy-healthy")
        self.assertEqual(seen, [45678])

    def test_legacy_scan_rejects_service_mismatch(self) -> None:
        with ping_server("other-service") as legacy_port:
            with self.assertRaises(self.resolver.EndpointResolutionError) as ctx:
                self._resolve(default_port=legacy_port, legacy_ports=(legacy_port,))

        self.assertEqual(ctx.exception.code, "SERVICE_MISMATCH")

    def test_timeout_candidate_raises_structured_error(self) -> None:
        with ping_server("test-runner-mcp", delay=0.25) as slow_port:
            with self.assertRaises(self.resolver.EndpointResolutionError) as ctx:
                self._resolve(default_port=slow_port, legacy_ports=(slow_port,), timeout=0.01)

        self.assertEqual(ctx.exception.code, "ENDPOINT_TIMEOUT")
        self.assertIn("candidates", ctx.exception.context)


if __name__ == "__main__":
    unittest.main(verbosity=2)
