#!/usr/bin/env bash
set -euo pipefail
umask 077
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
env_root="${PAGING_TEST_ENV_ROOT:-${TMPDIR:-/tmp}}"
env_dir="$(mktemp -d "${env_root}/paging-tests-XXXXXXXX")"
project="$(basename -- "$env_dir" | tr '[:upper:]' '[:lower:]')"
cleanup() {
  docker compose --project-name "$project" --project-directory "$env_dir" down --volumes --remove-orphans >/dev/null 2>&1 || return 1
  if docker ps -aq --filter "label=com.docker.compose.project=$project" | grep -q .; then
    echo 'Test containers remain after cleanup.' >&2
    return 1
  fi
  for kind in network volume; do
    if docker "$kind" ls -q --filter "label=com.docker.compose.project=$project" | grep -q .; then
      echo "Test $kind remains after cleanup." >&2
      return 1
    fi
  done
  if [[ -n "${PAGING_TEST_DB_PORT:-}" ]]; then
    python3 - <<'CHECK'
import os, subprocess
port = os.environ['PAGING_TEST_DB_PORT']
assert not any(line.split()[3].endswith(':' + port) for line in subprocess.check_output(['ss', '-lnt'], text=True).splitlines()[1:]), 'Test listener remains after cleanup'
CHECK
  fi
  rm -rf -- "$env_dir"
}
trap cleanup EXIT
python3 - "$env_dir" <<'PY'
from pathlib import Path
import secrets, sys
root = Path(sys.argv[1])
(root / '.env').write_text('POSTGRES_PASSWORD=' + secrets.token_hex(32) + '\n')
(root / '.env').chmod(0o600)
PY
cat > "$env_dir/docker-compose.yml" <<'YAML'
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: paging_test
      POSTGRES_DB: paging_tests
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?required}
    ports:
      - "127.0.0.1::5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U paging_test -d paging_tests"]
      interval: 1s
      timeout: 5s
      retries: 30
YAML
compose=(docker compose --project-name "$project" --project-directory "$env_dir")
"${compose[@]}" config --quiet
"${compose[@]}" up -d --wait
binding="$("${compose[@]}" port postgres 5432)"
[[ "$binding" =~ ^127\.0\.0\.1:[0-9]+$ ]]
export PAGING_TEST_DB_PORT="${binding##*:}"
python3 - <<'PY'
import os, subprocess
port = os.environ['PAGING_TEST_DB_PORT']
listeners = [line for line in subprocess.check_output(['ss', '-lnt'], text=True).splitlines() if line.split()[3].endswith(':' + port)]
assert listeners and all(line.split()[3] == '127.0.0.1:' + port for line in listeners), 'Test database is not loopback-only'
PY
# Read the credential into the child environment without placing it in a command line.
python3 - "$env_dir" "$repo_root" <<'PY'
from pathlib import Path
import json, os, subprocess, sys
import xml.etree.ElementTree as ET
root, repo = map(Path, sys.argv[1:])
password = (root / '.env').read_text().strip().split('=', 1)[1]
env = os.environ.copy()
connection_parameters = {
    'Host': '127.0.0.1', 'Port': env['PAGING_TEST_DB_PORT'],
    'Database': 'paging_tests', 'Username': 'paging_test', 'Password': password,
}
env['PAGING_TEST_POSTGRES_CONNECTION'] = ';'.join(f'{key}={value}' for key, value in connection_parameters.items())
results = repo / 'artifacts/test-results' / root.name
properties = ET.parse(repo / 'Directory.Build.props')
frameworks = properties.findtext('.//TargetFrameworks').split(';')
versions = properties.findtext('.//HotChocolateCompatibilityVersions').split(';')
command = [sys.executable, str(repo / 'tools/run-sanitized.py'), 'dotnet']
for version in versions:
    print(f'Testing Hot Chocolate {version}', flush=True)
    version_property = f'-p:HotChocolateTestVersion={version}'
    result = subprocess.run(command + ['build', str(repo / 'SelectionAwarePaging.sln'), '-c', 'Release', version_property], env=env)
    if result.returncode:
        raise SystemExit(result.returncode)
    for framework in frameworks:
        dependencies = json.loads((repo / 'tests/GtG.Open.HC.SelectionAwarePaging.Tests/bin/Release' / framework / 'GtG.Open.HC.SelectionAwarePaging.Tests.deps.json').read_text())['libraries']
        hc = {name: name.rsplit('/', 1)[1] for name in dependencies if name.startswith(('HotChocolate/', 'HotChocolate.'))}
        assert hc and set(hc.values()) == {version}, f'Mixed or unexpected HC dependencies: {hc}'
        target_results = results / version / framework
        result = subprocess.run(command + ['test', str(repo / 'SelectionAwarePaging.sln'), '-c', 'Release', '--framework', framework, version_property, '--no-build', '--no-restore', '--collect:XPlat Code Coverage', '--settings', str(repo / 'coverage.runsettings'), '--results-directory', str(target_results)], env=env)
        if result.returncode:
            raise SystemExit(result.returncode)
        reports = list(target_results.rglob('coverage.cobertura.xml'))
        assert len(reports) == 1, f'Expected one coverage report for {framework}'
        coverage = ET.parse(reports[0]).getroot()
        assert int(coverage.attrib['lines-valid']) > 0, 'Coverage instrumentation returned no lines'
        rate = float(coverage.attrib['line-rate'])
        assert rate >= 0.95, f'Library line coverage below 95%: {rate:.2%}'
        print(f'HC {version}, {framework} library line coverage: {rate:.2%}', flush=True)
PY
