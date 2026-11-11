# Framework and Hot Chocolate compatibility verification

Verified locally on 2026-11-11 for `GtG.Open.HC.SelectionAwarePaging` 16.6.0 with Hot Chocolate
16.6.0 and 16.7.5, using the upstream .NET SDK version
`11.0.100-preview.6.26359.118`. CI runs the same two-version, four-framework
verification on every push. No deployed application verification is claimed.

- .NET 8: 30 passed, 0 failed, 0 skipped for each HC version.
- .NET 9: 30 passed, 0 failed, 0 skipped for each HC version.
- .NET 10: 30 passed, 0 failed, 0 skipped for each HC version.
- .NET 11: 30 passed, 0 failed, 0 skipped for each HC version.
- Total: 240 successful test executions.
- Library line coverage: 134/140 (95.71%) in all eight combinations.
- Library branch coverage: 86.58% in all eight combinations.
- The package contains separate `net8.0`, `net9.0`, `net10.0` and `net11.0`
  assemblies, XML documentation and dependency groups. Each group declares
  the HC dependency range `[16.6.0,17.0.0)`. Package metadata reports version
  16.6.0, aligned with the minimum supported HC version.
- A standalone consumer restored the actual NuGet package into a fresh package
  cache for each HC version, asserted that its selected library assembly matches
  its own target framework, and verified item IDs, total count and both page
  flags in all eight combinations. All HC dependencies in each test and consumer
  resolved to the selected version. The same package, compiled against HC
  16.6.0, was used for both consumer versions. The observed runtimes were 8.0.29,
  9.0.18, 10.0.11 and the .NET 11 preview runtime supplied by the SDK above.
- All project-owned namespaces use the `GtG.Open.` prefix. No application
  assemblies are bundled.
- Build/test output masking and failure-code propagation were checked.
- Temporary PostgreSQL containers, networks, volumes and listeners were removed.
- The paging implementation is unchanged; the namespace/package rename preserves
  the HC compatibility range and four framework targets. The original extraction
  also passed an independent read-only source comparison.

## Executed behavior matrix

| Scenario | Verified response and execution |
| --- | --- |
| Count-only | Exact filtered count, count SQL only, zero materialized items |
| Items-only | Exact page and SQL limit, no count/lookahead |
| Items plus next-page flag | One lookahead row, trimmed from serialized response |
| Items, count and flags | Exact page, count and flags; no extra row |
| Count plus page info | Count-only SQL with correct flags |
| Next-page flag only | Existence SQL, no page materialization |
| Previous-page flag or typename only | No source execution |
| Aliases, fragments and directives | Effective selections control execution |
| Custom segment field | Required page items are materialized |
| Paging observer | Items and page metadata reach the observer |
| Filtering and sorting | Correct filtered counts and ordered pages |
| Empty/end pages, zero take and unbounded take | Exact wire values and SQL shape |
| Enumerable and executable sources | Correct items, counts and flags |
| Schema parity | Unchanged schema compared with the built-in provider |
| Real HTTP count-only request | Exact JSON count, no item materialization |

## Commands

```sh
tools/test.sh
python3 tools/run-sanitized.py dotnet pack src/GtG.Open.HC.SelectionAwarePaging -c Release -o artifacts/packages
python3 tools/check-package.py
python3 tools/check-consumer.py
python3 -B -m unittest discover -s tools/privacy -p 'test_*.py'
python3 -B tools/privacy/check.py --staged
python3 -B tools/privacy/check.py --worktree
python3 -B tools/privacy/check.py --metadata --history --output artifacts/packages
```

## Privacy verification

The maintained DotNetJq scanner and its regression suite were reused unchanged;
the [local procedure](../tools/privacy/README.md) documents their pinned origin.
The 17 upstream scanner regression tests passed. The initial release history
is consolidated into one commit. Source and complete fetched history use the
strict scanner without historical exceptions.

The audit covers source, Git identities/messages/refs, package metadata and
contents, filenames, nested archives and symbols, with an additional private
denylist stored outside the repository. Earlier public GitHub CI logs and all
six uploaded package artifacts were also inspected. No unresolved disclosure
was found; path matches in those logs were documented GitHub-hosted runner paths.
There were no existing pull requests or GitHub releases to audit.
Direct assembly/PDB metadata inspection also confirmed the new assembly names
and namespaces, seven mapped document paths per target, and no embedded resources
or SourceLink records.

CI now fetches full history, runs privacy regression tests, and scans the exact
package and library symbols before artifact upload. The release workflow repeats
the source/history and final-package check before requesting NuGet credentials.
These checks do not constitute a guarantee against every possible encoded or
previously unknown secret.

## Remaining limits

The inherited suite does not directly exercise integer-overflow boundaries,
negative/unsupported source fallbacks, cancellation, or unknown result shapes.
Hot Chocolate versions within the allowed range other than 16.6.0 and 16.7.5
have not been verified. No consuming business application has been changed,
deployed or tested as part of this standalone extraction.
Downstream migration remains outstanding. Check the release workflow and live
NuGet listing for publication status; successful tests alone do not prove a push.
The repository is hosted at https://github.com/gtg-open/selection-aware-paging.
