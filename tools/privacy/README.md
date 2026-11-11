# Publication privacy checks

`check.py` and `test_check.py` are unmodified copies of the maintained
[DotNetJq scanner](https://github.com/gtg-open/dotnetjq/tree/50468ee5670e787ca92b2e374d6f4774d0d42d18/tools/privacy),
under the same MIT license. It checks exact Git index blobs, tracked working
files, reachable history, author/committer/tag identities, ref names, filenames,
symlink targets, and selected artifacts including nested ZIP/NuGet, tar and gzip
contents. It scans UTF-8 and UTF-16 text in binaries and symbols. Unsupported,
encrypted, malformed or oversized inputs fail the check.

Before committing and publishing, review the actual diff and run:

```sh
python3 -B -m unittest discover -s tools/privacy -p 'test_*.py'
python3 -B tools/privacy/check.py --staged
python3 -B tools/privacy/check.py --worktree
python3 -B tools/privacy/check.py --metadata --history
python3 -B tools/privacy/check.py --metadata --output artifacts/packages
```

Repeat `--output` for the exact symbols, logs and reports being shared. Keep
private logs and denylist files outside Git. `--denylist` accepts an external
UTF-8 file of literal private values, one per line; findings never print those
values. The minimum denylist entry length is four characters.

CI fetches full history and checks source before building, then scans the package
and library symbols before upload. `--github-hosted` may be used only in actual
GitHub-hosted jobs; it recognizes documented public runner home paths in output
artifacts, while repository and history scans stay strict. It must not be enabled
by fabricating environment variables in local or self-hosted runs.

## Scope and limits

The initial release history is consolidated into one commit. Source, index,
history and outputs use the same unmodified scanner without historical
exceptions. Do not add a broad filename, directory or rule exemption to make
a scan pass.

Git scans cover locally reachable refs, not hosting-service surfaces. Published
CI logs, artifacts, releases and other remote text must be audited separately.
Legitimate upstream attribution and approved public maintainer identities remain.
These checks reduce disclosure risk; they do not prove the absence of arbitrary
personal names, unknown secrets or every possible encoded representation.
