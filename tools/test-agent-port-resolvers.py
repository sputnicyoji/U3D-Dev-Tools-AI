import importlib.util
import json
import os
import tempfile
import threading
import unittest
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch
from urllib.error import URLError


ROOT = Path(__file__).resolve().parents[1]
RESOLVER_PATH = ROOT / "Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/port_resolver.py"


def load_resolver():
    spec = importlib.util.spec_from_file_location("agent_port_resolver", RESOLVER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class _PingHandler(BaseHTTPRequestHandler):
    service_id = ""

    def _send(self):
        body = json.dumps({"serviceId": self.service_id, "ok": True}).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path != "/ping":
            self.send_error(404)
            return
        self._send()

    def do_POST(self):
        if self.path != "/ping":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)
        self._send()

    def log_message(self, format, *args):
        return


class _Server(ThreadingHTTPServer):
    allow_reuse_address = True


@contextmanager
def ping_server(service_id):
    handler = type("Handler", (_PingHandler,), {"service_id": service_id})
    server = _Server(("127.0.0.1", 0), handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield server.server_address[1]
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


def write_json(path: Path, payload) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload), encoding="utf-8")


def make_project(root: Path) -> Path:
    (root / "ProjectSettings").mkdir(parents=True, exist_ok=True)
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f2c1", encoding="utf-8")
    return root


def registry_path(local_appdata: Path) -> Path:
    return local_appdata / "Yoji" / "U3D-Dev-Tools-AI" / "instances.json"


class PortResolverTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.project = make_project(self.root / "Project")
        self.local_appdata = self.root / "LocalAppData"
        self.env = patch.dict(os.environ, {"LOCALAPPDATA": str(self.local_appdata)})
        self.env.start()
        self.addCleanup(self.env.stop)
        self.resolver = load_resolver()

    def _resolve(self, *, port=None, project=None, pid=None, timeout=1.0):
        return self.resolver.resolve_endpoint(
            "test-runner-mcp",
            "127.0.0.1",
            port,
            21890,
            project,
            pid,
            timeout,
        )

    def test_project_ports_json_resolves_single_instance(self):
        with ping_server("test-runner-mcp") as port:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {
                    "instances": [
                        {
                            "serviceId": "test-runner-mcp",
                            "host": "127.0.0.1",
                            "port": port,
                            "processId": 101,
                            "projectRoot": str(self.project),
                            "instanceId": "a",
                        }
                    ]
                },
            )

            host, resolved_port, source = self._resolve(project=str(self.project))

            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, port)
            self.assertEqual(source, "project-registry")

    def test_multiple_matching_records_require_pid(self):
        with ping_server("test-runner-mcp") as port1, ping_server("test-runner-mcp") as port2:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {
                    "instances": [
                        {
                            "serviceId": "test-runner-mcp",
                            "host": "127.0.0.1",
                            "port": port1,
                            "processId": 101,
                            "projectRoot": str(self.project),
                            "instanceId": "a",
                        },
                        {
                            "serviceId": "test-runner-mcp",
                            "host": "127.0.0.1",
                            "port": port2,
                            "processId": 202,
                            "projectRoot": str(self.project),
                            "instanceId": "b",
                        },
                    ]
                },
            )

            with self.assertRaises(SystemExit) as ctx:
                self._resolve(project=str(self.project))

            text = str(ctx.exception)
            self.assertIn("ambiguous test-runner-mcp instances", text)
            self.assertIn("pid=101", text)
            self.assertIn("pid=202", text)

    def test_pid_disambiguates(self):
        with ping_server("test-runner-mcp") as port1, ping_server("test-runner-mcp") as port2:
            write_json(
                self.project / ".u3d-ai-linker" / "ports.json",
                {
                    "instances": [
                        {
                            "serviceId": "test-runner-mcp",
                            "host": "127.0.0.1",
                            "port": port1,
                            "processId": 101,
                            "projectRoot": str(self.project),
                            "instanceId": "a",
                        },
                        {
                            "serviceId": "test-runner-mcp",
                            "host": "127.0.0.1",
                            "port": port2,
                            "processId": 202,
                            "projectRoot": str(self.project),
                            "instanceId": "b",
                        },
                    ]
                },
            )

            host, resolved_port, source = self._resolve(project=str(self.project), pid=202)

            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, port2)
            self.assertEqual(source, "project-registry")

    def test_malformed_json_falls_back_to_legacy_default(self):
        ports = self.project / ".u3d-ai-linker" / "ports.json"
        ports.parent.mkdir(parents=True, exist_ok=True)
        ports.write_text("{broken", encoding="utf-8")

        host, resolved_port, source = self._resolve(project=str(self.project))

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, 21890)
        self.assertEqual(source, "legacy-default")

    def test_stale_registry_candidate_is_skipped_then_falls_back(self):
        malformed = self.project / ".u3d-ai-linker" / "ports.json"
        malformed.parent.mkdir(parents=True, exist_ok=True)
        malformed.write_text("{broken", encoding="utf-8")

        with ping_server("other-service") as port:
            write_json(
                registry_path(self.local_appdata),
                {
                    "instances": [
                        {
                            "serviceId": "test-runner-mcp",
                            "host": "127.0.0.1",
                            "port": port,
                            "processId": 333,
                            "projectRoot": str(self.project),
                            "instanceId": "stale",
                        }
                    ]
                },
            )
            host, resolved_port, source = self._resolve(project=str(self.project))
            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, 21890)
            self.assertEqual(source, "legacy-default")

        write_json(
            registry_path(self.local_appdata),
            {
                "instances": [
                    {
                        "serviceId": "test-runner-mcp",
                        "host": "127.0.0.1",
                        "port": 22001,
                        "processId": 444,
                        "projectRoot": str(self.project),
                        "instanceId": "dead",
                    }
                ]
            },
        )
        with patch.object(self.resolver.urllib.request, "urlopen", side_effect=URLError("boom")):
            host, resolved_port, source = self._resolve(project=str(self.project))
            self.assertEqual(host, "127.0.0.1")
            self.assertEqual(resolved_port, 21890)
            self.assertEqual(source, "legacy-default")

    def test_explicit_port_wins(self):
        write_json(
            self.project / ".u3d-ai-linker" / "ports.json",
            {
                "instances": [
                    {
                        "serviceId": "test-runner-mcp",
                        "host": "127.0.0.1",
                        "port": 21900,
                        "processId": 111,
                        "projectRoot": str(self.project),
                        "instanceId": "a",
                    }
                ]
            },
        )
        write_json(
            registry_path(self.local_appdata),
            {
                "instances": [
                    {
                        "serviceId": "test-runner-mcp",
                        "host": "127.0.0.1",
                        "port": 22000,
                        "processId": 222,
                        "projectRoot": str(self.project),
                        "instanceId": "b",
                    }
                ]
            },
        )

        host, resolved_port, source = self._resolve(port=29999, project=str(self.project), pid=111)

        self.assertEqual(host, "127.0.0.1")
        self.assertEqual(resolved_port, 29999)
        self.assertEqual(source, "explicit")


if __name__ == "__main__":
    unittest.main(verbosity=2)
