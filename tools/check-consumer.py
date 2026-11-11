#!/usr/bin/env python3
"""Exercise the same packed library with each verified HC version and target."""
from pathlib import Path
import json
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parent.parent
properties = ET.parse(repo / 'Directory.Build.props')
frameworks = properties.findtext('.//TargetFrameworks').split(';')
versions = properties.findtext('.//HotChocolateCompatibilityVersions').split(';')
command = [sys.executable, str(repo / 'tools/run-sanitized.py'), 'dotnet']
project = repo / 'tests/PackageConsumer'
(repo / 'artifacts').mkdir(exist_ok=True)
for version in versions:
    print(f'Package consumer with Hot Chocolate {version}', flush=True)
    version_property = f'-p:HotChocolateTestVersion={version}'
    with tempfile.TemporaryDirectory(prefix='package-consumer-', dir=repo / 'artifacts') as cache:
        result = subprocess.run(command + ['restore', str(project), version_property, '--source', str(repo / 'artifacts/packages'), '--source', 'https://api.nuget.org/v3/index.json', '--packages', cache])
        if result.returncode:
            raise SystemExit(result.returncode)
        for framework in frameworks:
            result = subprocess.run(command + ['run', '--project', str(project), '-c', 'Release', '--framework', framework, '--no-restore', version_property])
            if result.returncode:
                raise SystemExit(result.returncode)
            dependencies = json.loads((project / 'bin/Release' / framework / 'PackageConsumer.deps.json').read_text())['libraries']
            hc = {name: name.rsplit('/', 1)[1] for name in dependencies if name.startswith(('HotChocolate/', 'HotChocolate.'))}
            assert hc and set(hc.values()) == {version}, f'Mixed or unexpected HC dependencies: {hc}'
            print(f'{framework}: all {len(hc)} HC dependencies resolved to {version}', flush=True)
