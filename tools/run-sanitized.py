#!/usr/bin/env python3
"""Run a command, preserving its exit status and removing private output paths."""
from pathlib import Path
import os
import re
import subprocess
import sys
import tempfile

if len(sys.argv) < 2:
    raise SystemExit('Usage: run-sanitized.py command [arguments...]')
repo = Path(__file__).resolve().parent.parent
replacements = [(str(repo), '.'), (str(Path.home()), '<home>'), (tempfile.gettempdir(), '<tmp>')]
if dotnet_root := os.environ.get('DOTNET_ROOT'):
    replacements.insert(1, (dotnet_root, '<dotnet>'))
connection = os.environ.get('PAGING_TEST_POSTGRES_CONNECTION', '')
credential_match = re.search(r'(?i)(?:^|;)\s*Password=([^;]+)', connection)
if credential_match:
    replacements.insert(0, (credential_match.group(1), '<redacted>'))
process = subprocess.Popen(sys.argv[1:], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors='replace')
assert process.stdout is not None
for line in process.stdout:
    for original, replacement in replacements:
        line = line.replace(original, replacement)
    line = re.sub(r'(?i)\b(Password|PWD)\s*=\s*[^;\s]+', r'\1=<redacted>', line)
    print(line, end='', flush=True)
raise SystemExit(process.wait())
