from pathlib import Path
import filecmp

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/port_resolver.py'
TARGETS = [
    ROOT / 'Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/port_resolver.py',
    ROOT / 'Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/port_resolver.py',
]


def main() -> int:
    source_bytes = SOURCE.read_bytes()
    for target in TARGETS:
        target.write_bytes(source_bytes)
    for target in TARGETS:
        if not filecmp.cmp(SOURCE, target, shallow=False):
            raise SystemExit(f'copy mismatch: {target}')
    print(f'synced {len(TARGETS)} resolver copies')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
