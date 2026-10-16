#!/usr/bin/env python3
"""Checks the private endpoint without displaying credentials or calling Gemini."""
import json
from pathlib import Path
import urllib.request
root=Path(__file__).resolve().parents[1]
config=json.loads((root/'Assets/StreamingAssets/guidedreality.local.json').read_text())
request=urllib.request.Request(config['samEndpoint'].rstrip('/')+'/health',headers={'Authorization':'Bearer '+config['samToken']})
with urllib.request.urlopen(request,timeout=15) as response:
    print(response.status,json.load(response))
