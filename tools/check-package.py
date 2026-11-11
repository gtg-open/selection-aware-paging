#!/usr/bin/env python3
"""Inspect release packages for payload, dependency and private-metadata mistakes."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

package_id = 'GtG.Open.HC.SelectionAwarePaging'
properties = ET.parse('Directory.Build.props')
version = properties.findtext('.//Version')
minimum = properties.findtext('.//HotChocolateMinimumVersion')
dependency_range = properties.findtext('.//HotChocolateDependencyRange').replace('$(HotChocolateMinimumVersion)', minimum)
packages = list(Path('artifacts/packages').glob('*.nupkg'))
assert Path('artifacts/packages') / f'{package_id}.{version}.nupkg' in packages, 'Current package not found; run dotnet pack first.'
frameworks = set(properties.findtext('.//TargetFrameworks').split(';'))
for package in packages:
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        expected_assemblies = {f'lib/{framework}/{package_id}.dll' for framework in frameworks}
        assert {name for name in names if name.endswith('.dll')} == expected_assemblies, 'Missing target assembly or unexpected dependency assembly bundled'
        assert all(f'lib/{framework}/{package_id}.xml' in names for framework in frameworks), 'Missing target XML documentation'
        assert 'README.md' in names and 'LICENSE' in names and 'THIRD_PARTY_NOTICES.md' in names
        ns = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
        metadata = ET.fromstring(archive.read(f'{package_id}.nuspec')).find('n:metadata', ns)
        assert metadata.findtext('n:id', namespaces=ns) == package_id
        assert metadata.findtext('n:version', namespaces=ns) == version
        assert metadata.findtext('n:authors', namespaces=ns) == 'GtG Open Maintainers'
        assert metadata.findtext('n:license', namespaces=ns) == 'MIT'
        groups = metadata.findall('n:dependencies/n:group', ns)
        assert len(groups) == len(frameworks) and {group.attrib['targetFramework'] for group in groups} == frameworks, 'Incorrect dependency target groups'
        for group in groups:
            assert [(d.attrib['id'], d.attrib['version'].replace(' ', '')) for d in group.findall('n:dependency', ns)] == [('HotChocolate.Types.OffsetPagination', dependency_range)]
        for name in names:
            data = archive.read(name)
            for encoding in ('utf-8', 'utf-16-le'):
                text = data.decode(encoding, errors='ignore')
                assert not re.search(r'/(?:home|Users)/|[A-Za-z]:\\Users\\|bitbucket\.org|atlassian\.net', text, re.I), f'Private metadata found in {name}'
    print(f'Package payload, dependency and privacy checks passed: {package.name}')
# Keep the shipping assemblies compiled against the minimum supported HC version.
assets = json.loads((Path('src') / package_id / 'obj/project.assets.json').read_text())
assert f'HotChocolate.Types.OffsetPagination/{minimum}' in assets['libraries'], 'Library was not built against the minimum HC version'
# Portable symbols must also contain mapped source paths only.
for framework in frameworks:
    pdb = Path('src') / package_id / 'bin/Release' / framework / f'{package_id}.pdb'
    data = pdb.read_bytes()
    for encoding in ('utf-8', 'utf-16-le'):
        assert not re.search(r'/(?:home|Users)/|[A-Za-z]:\\Users\\', data.decode(encoding, errors='ignore'), re.I), 'Private path in symbols'
