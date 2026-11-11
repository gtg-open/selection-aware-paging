# Contributor instructions

All project-owned C# namespaces and public package IDs use the `GtG.Open.` prefix.
Preserve the selection-aware paging contract and verify supported HC/framework
combinations before changing compatibility claims.

Before committing or publishing, review both the working and staged diffs,
including renamed files, generated outputs and Git metadata. Keep personal
identities, workstation paths, private endpoints and credentials out of source,
commit/tag metadata, logs and artifacts. Use the configured approved public
maintainer identity; preserve legitimate upstream attribution and license notices.

Follow [the privacy verification procedure](tools/privacy/README.md). Run scanner
tests and the staged, worktree, full-history and explicit publication-output
checks. Fetch all public branches and tags before a history audit. Inspect the
final package bytes, symbols and metadata as well as scanner results. Resolve
findings before publication; do not add broad exceptions or weaken a rule to make
a check pass. Historical false-positive exceptions require an exact file hash,
path and rule, documented review, and regression coverage.

Use the release workflow for NuGet publication. A successful verification run
does not imply the package has been published; verify NuGet indexing and a fresh
consumer restore before reporting publication complete.
