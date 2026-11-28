r"""Which local files the MCP server itself may read: the SewerGEMS / WaterGEMS workbooks and CSV files.

Same rules as the plugin (plugin/Core/FilePolicy.cs), so a client meets one boundary whichever side opens the file:

  - the path must be absolute; device paths (\\?\, \\.\) are refused;
  - nothing is read from the bridge's own folder (bridge.json holds the session token);
  - when %LOCALAPPDATA%\C3dMcpBridge\policy.json exists,
        {"allowedRoots": ["D:\\Projects", "\\\\server\\share\\jobs"]}
    the file must lie under one of those folders. A policy file that cannot be read as that JSON refuses every file
    (fail closed).

Files larger than MAX_INPUT_BYTES are refused before they are parsed, and readers stop at MAX_ROWS rows: both run in a
worker thread that cannot be interrupted once it is inside the parser.
"""
from __future__ import annotations

import json
import os
from pathlib import Path

MAX_INPUT_BYTES = 64 * 1024 * 1024
MAX_ROWS = 200_000
MAX_POLICY_BYTES = 64 * 1024


def _data_folder() -> Path | None:
    base = os.environ.get("LOCALAPPDATA")
    return Path(base) / "C3dMcpBridge" if base else None


def policy_file() -> Path | None:
    override = os.environ.get("C3D_POLICY_FILE")   # used by the tests
    if override:
        return Path(override)
    folder = _data_folder()
    return folder / "policy.json" if folder else None


def allowed_roots() -> list[Path] | None:
    """The allowed folders, or None when there is no policy file (any folder the Windows user can read)."""
    path = policy_file()
    if path is None or not path.is_file():
        return None
    try:
        with path.open("rb") as f:
            raw = f.read(MAX_POLICY_BYTES + 1)
        if len(raw) > MAX_POLICY_BYTES:
            raise ValueError("it is too large")
        data = json.loads(raw.decode("utf-8-sig"))
        roots = data.get("allowedRoots") if isinstance(data, dict) else None
        if not isinstance(roots, list) or not roots:
            raise ValueError('it needs {"allowedRoots": ["C:\\\\Projects", ...]}')
        out = []
        for root in roots:
            if not isinstance(root, str) or not root.strip() or not Path(root).is_absolute() or _is_device(root):
                raise ValueError("every root must be a full folder path")
            out.append(Path(os.path.abspath(root)))
        return out
    except (OSError, ValueError) as exc:
        raise ValueError(f"File access is refused because {path} is not usable: {exc}. Fix or delete the file.") from None


def _is_device(text: str) -> bool:
    t = text.replace("/", "\\")
    return t.startswith("\\\\?\\") or t.startswith("\\\\.\\")


def _under(path: Path, folder: Path) -> bool:
    p, f = os.path.normcase(str(path)), os.path.normcase(str(folder)).rstrip("\\/")
    return p == f or (p.startswith(f) and len(p) > len(f) and p[len(f)] in "\\/")


def readable(text: object, what: str) -> Path:
    """The file named by a client parameter, checked against the policy. Raises ValueError with the reason."""
    if not isinstance(text, str) or not text.strip():
        raise ValueError(f"Give {what}.")
    if _is_device(text):
        raise ValueError(f"{what}: device paths are not accepted.")
    given = Path(text).expanduser()
    if not given.is_absolute():
        raise ValueError(f"{what}: give a full path such as C:\\Projects\\export.xlsx (got '{text}').")
    # Resolve links and '..' before comparing, so a link inside an allowed folder cannot point outside it.
    try:
        path = given.resolve()
    except OSError as exc:
        raise ValueError(f"{what}: '{text}' is not a usable path ({exc}).") from None
    folder = _data_folder()
    if folder is not None and _under(path, Path(os.path.abspath(folder))):
        raise ValueError(f"{what}: the bridge's own folder is not available to MCP clients.")
    roots = allowed_roots()
    if roots is not None and not any(_under(path, r.resolve()) for r in roots):
        raise ValueError(f"{what}: {path} is outside the folders this PC allows for MCP "
                         f"({'; '.join(str(r) for r in roots)}). The user can change them in {policy_file()}.")
    if not path.is_file():
        raise ValueError(f"{what}: file not found: {path}")
    size = path.stat().st_size
    if size > MAX_INPUT_BYTES:
        raise ValueError(f"{what}: {path.name} is {size / 1048576:.0f} MB; the limit is {MAX_INPUT_BYTES // 1048576} MB.")
    return path


def bounded(rows, what: str):
    """Yield rows, refusing a table longer than MAX_ROWS instead of loading it all."""
    for count, row in enumerate(rows, 1):
        if count > MAX_ROWS:
            raise ValueError(f"{what} has more than {MAX_ROWS:,} rows; export a smaller selection.")
        yield row
