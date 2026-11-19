"""Development aid: put any live calls here and run run_script.bat (output: results\\script.txt)."""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "server"))
from bridge_client import call  # noqa: E402

print(json.dumps(call("ping"), indent=1))
