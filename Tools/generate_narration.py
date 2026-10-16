#!/usr/bin/env python3
"""Generate bundled narration on macOS: python3 Tools/generate_narration.py."""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = root / 'Assets/3DPrinter.json'
out = root / 'Assets/Resources/InstructionNarration'
out.mkdir(parents=True, exist_ok=True)
steps = json.loads(source.read_text())['instructions']
for i, step in enumerate(steps, 1):
    text = step['instruction'].strip()
    name = hashlib.sha256(text.encode('utf-8')).hexdigest()
    target = out / (name + '.wav')
    # Expand abbreviations only for pronunciation; the hash uses the original instruction.
    spoken = text.replace('1m15s', 'one minute and fifteen seconds').replace('[PLA]', 'P L A').replace('[Unload Filament] -', 'Unload Filament, then')
    with tempfile.TemporaryDirectory() as tmp:
        aiff = Path(tmp) / 'speech.aiff'
        subprocess.run(['/usr/bin/say', '-v', 'Samantha', '-r', '165', '-o', str(aiff), spoken], check=True)
        subprocess.run(['/usr/bin/afconvert', '-f', 'WAVE', '-d', 'LEI16@24000', '-c', '1', str(aiff), str(target)], check=True)
    print(f'Step {i}: {target.name}')
print('Generated all narration. Regenerate after editing instruction text.')
