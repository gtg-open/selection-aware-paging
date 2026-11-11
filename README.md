# GtG.Open.HC.SelectionAwarePaging

**Avoid loading page rows when your GraphQL client only asks for a count.**

An offset-paging extension for Hot Chocolate that makes database execution follow
the fields selected by the client: `items`, `totalCount` and `pageInfo`. It skips
unnecessary row fetching, object allocation and next-page lookahead while keeping
the existing GraphQL schema and paging arguments.

## Why this library exists

A dashboard counter, a tab badge or a pagination control often needs a count or
page metadata from an existing paginated field. Selecting fewer GraphQL fields
reduces the response, but it does not necessarily reduce the database work.

In Hot Chocolate **16.6.0**, the built-in **queryable offset-paging provider** loads
the requested page plus one lookahead row even when the client has not selected
`items`. If `totalCount` is selected, it then executes a count query as well.
Those page rows are fetched and turned into .NET objects even though the client
never receives them.

That extra work means database reads, data transfer and allocations for a
count-only request. Its cost grows with the page size, row width and query
complexity. A large configured default page size can make even a small dashboard
counter unexpectedly expensive.

This library was created to eliminate that unnecessary work and make the
optimization reusable across applications. It inspects the effective GraphQL
selection and runs only the item, count or existence queries needed to answer it.
Existing paginated fields can serve both full pages and lightweight metadata
requests, without adding dedicated count-only resolvers to every API.

### Example: a counter that accidentally loads a page

Consider a `products` field backed by an ordered database query:

```graphql
{
  products(take: 50, where: { isActive: { eq: true } }) {
    totalCount
  }
}
```

The client needs one number. The built-in provider still fetches up to **51
product rows**, materializes them, and then counts the filtered products.
With this library, the same request executes **only the count query** and
materializes **zero product rows**.

Illustrative SQL for this request; exact SQL depends on the query provider:

```sql
-- Built-in queryable offset paging in Hot Chocolate 16.6.0
SELECT * FROM products WHERE is_active = TRUE ORDER BY id LIMIT 51;
SELECT COUNT(*) FROM products WHERE is_active = TRUE;

-- With GtG.Open.HC.SelectionAwarePaging
SELECT COUNT(*) FROM products WHERE is_active = TRUE;
```

Both return the same `totalCount`. On a dashboard requesting several filtered
counters through aliases, each counter avoids its otherwise unused page fetch.
The same principle applies to `pageInfo`: `hasPreviousPage` follows from the
offset, and `hasNextPage` can use an existence query when no items are needed.

The upstream behavior is visible in the versioned
[paging algorithm](https://github.com/ChilliCream/graphql-platform/blob/16.6.0/src/HotChocolate/Core/src/Types.OffsetPagination/OffsetPaginationAlgorithm.cs)
and [queryable handler](https://github.com/ChilliCream/graphql-platform/blob/16.6.0/src/HotChocolate/Core/src/Types.OffsetPagination/QueryableOffsetPagingHandler.cs).
Hot Chocolate already makes `COUNT` conditional on `totalCount` being selected;
this library extends selection awareness to page loading and lookahead.

## What work does it save?

For ordinary collection segments without paging observers or item-dependent
custom fields, with a finite page size of `take`:

| Client selects | Built-in queryable provider, HC 16.6.0 | With this library |
| --- | --- | --- |
| `totalCount` only | Fetch up to `take + 1` rows, then count | Count only; no page rows |
| `items` only | Fetch up to `take + 1` rows | Fetch up to `take` rows |
| `items` + `hasNextPage` | Fetch up to `take + 1` rows | Same lookahead; it is needed here |
| `items` + `totalCount` + `hasNextPage` | Fetch up to `take + 1` rows, then count | Fetch up to `take` rows + count; derive the flag from the count |
| `totalCount` + `pageInfo` | Fetch up to `take + 1` rows, then count | Count only; derive both flags |
| `hasNextPage` only | Fetch up to `take + 1` rows | Existence query after the page; no page rows |
| `hasPreviousPage` only, or `__typename` only | Fetch up to `take + 1` rows | No paging query |

These savings concern execution of the query returned by the resolver. The
resolver and its middleware still run. The library preserves filters and sorting;
queries must remain deferred until the paging provider executes them.

It is useful for dashboards with several counters, list views that request only
some paging fields, and APIs with large default page sizes. Database indexing,
count-query cost and the cost of large offsets still matter. The tests verify SQL
shape, returned values and materialized rows; actual latency savings depend on
the application's queries and database.

## Using the library

Register the provider once in the GraphQL server. Existing `[UseOffsetPaging]`
fields keep their `skip`, `take`, `items`, `totalCount` and `pageInfo` contract.

### Compatibility

The package ships separate assemblies for **`net8.0`, `net9.0`, `net10.0` and
`net11.0`**, matching the targets of `HotChocolate.Types.OffsetPagination` 16.6.0.
NuGet selects the matching assembly for the consuming application. The minimum
supported framework remains .NET 8; the paging dependency does not provide a
`netstandard2.0` target.

Library version **16.6.0** aligns with the minimum supported Hot Chocolate
version. Its dependency range is **`[16.6.0,17.0.0)`**: applications can use
Hot Chocolate 16.6.0 or a later 16.x release without replacing this library.
All Hot Chocolate packages in the consuming application must use the same version.
CI verifies **16.6.0 and 16.7.5** on all four target frameworks, including use of
the same packaged library with both versions. Other versions within the range
are allowed but have not been verified. The library is built against 16.6.0 to
preserve its minimum supported version. This package does not add an EF Core,
linq2db, database-provider or application-service dependency.

### Installation

Package ID: `GtG.Open.HC.SelectionAwarePaging`. Release version: **16.6.0**.

```sh
dotnet add package GtG.Open.HC.SelectionAwarePaging --version 16.6.0
```

For a build from source, download the `package` artifact from a successful
[CI run](https://github.com/gtg-open/selection-aware-paging/actions/workflows/ci.yml)
or run `dotnet pack` as shown below. Add the directory containing the `.nupkg`
to your consumer's NuGet sources alongside NuGet.org before installing.

### Registration

```csharp
using GtG.Open.HC.SelectionAwarePaging;

services.AddGraphQLServer()
    .AddSelectionAwareOffsetPaging(defaultProvider: true)
    .AddQueryType<Query>()
    .ModifyPagingOptions(options => options.IncludeTotalCount = true);
```

Use the standard `[UseOffsetPaging]`, `[UseFiltering]` and `[UseSorting]`
attributes on resolvers returning deferred `IQueryable<T>` values. Keep their data
context alive until GraphQL execution completes. Register the provider once;
`IncludeTotalCount` remains a standard Hot Chocolate option.

For a named provider, call `AddSelectionAwareOffsetPaging("selection-aware")`
and use `[UseOffsetPaging(ProviderName = "selection-aware")]`.

## Behavior and limits

Aliases, fragments and directives use the effective GraphQL selection. Custom
segment fields conservatively require page items. Custom fields that read page
metadata must also select the corresponding standard `pageInfo` fields. Paging
observers receive items and page metadata even for metadata-only selections.

Queryable sources, reference-type enumerables and executable wrappers are
supported. Unsupported sources and negative arguments retain the upstream
fallback behavior. Ordering and consistency between separate item/count queries
remain the responsibility of the application and its query provider.

Counts and existence checks currently use synchronous query-provider operations
on the thread pool, matching the extracted implementation. Item queries use
`IAsyncEnumerable<T>` where available. Cancellation behavior depends on the
underlying provider; cancellation does not forcibly interrupt a running
synchronous database command.

## Build and test

```sh
dotnet build -c Release
tools/test.sh
```

`tools/test.sh` provisions an isolated Compose PostgreSQL instance with a fresh
password and a dynamic loopback-only port, runs the same PostgreSQL, GraphQL and
HTTP tests on **all four target frameworks with HC 16.6.0 and 16.7.5**, then
removes its containers and data. Each combination must meet the 95% library
line-coverage gate. Docker Compose, Python 3 and `ss` are required.

Building all targets requires the **.NET 11 SDK** selected in [global.json](global.json),
currently `11.0.100-preview.6.26359.118`, matching the upstream 16.6.0 build.
Running the full test suite also requires the .NET and ASP.NET Core runtimes for
.NET 8, 9, 10 and 11. This build-tool requirement does not raise the runtime
requirement for a .NET 8 consumer.

The shared target and HC compatibility lists live in [Directory.Build.props](Directory.Build.props).
The library, tests and package consumer all use it, and CI installs the required
SDKs. After packing, verify the package and its consumption on every target:

```sh
python3 tools/check-package.py
python3 tools/check-consumer.py
```

The consumer check uses a fresh package cache for each HC version, asserts that
NuGet selected the matching target assembly and aligned HC dependencies, and
verifies items, total count and both page flags on every target. The package is
built once against HC 16.6.0 and reused for both consumer versions.

For manually provisioned test infrastructure, the integration and real HTTP tests require an isolated PostgreSQL database named
`paging_tests`. Set `PAGING_TEST_POSTGRES_CONNECTION` locally; only loopback hosts
and the dedicated database name are accepted. Tests create and drop unique
schemas. No credentials are stored in this repository. HTTP tests bind only to
`127.0.0.1` on a dynamically assigned port.

The test suite checks serialized GraphQL responses, SQL shapes, materialization,
filtering, sorting, observers, custom segment fields and schema parity against
the built-in provider.

```sh
dotnet pack src/GtG.Open.HC.SelectionAwarePaging -c Release -o artifacts/packages
```

See [local verification](docs/verification.md) for results and remaining test gaps.

The [GitHub CI workflow](https://github.com/gtg-open/selection-aware-paging/actions/workflows/ci.yml)
builds a downloadable `package` artifact and verifies it in a separate consumer.
The [release workflow](.github/workflows/release.yml) supports verified NuGet
publication using Trusted Publishing; see [release setup](docs/releasing.md).
CI also checks source history, package contents and symbols using the
[publication privacy checks](tools/privacy/README.md).

## Project and license

HC denotes compatibility with Hot Chocolate. This is an independent extension,
not an official ChilliCream package. It uses the MIT license and retains the
upstream notices. See [LICENSE](LICENSE) and
[third-party notices](THIRD_PARTY_NOTICES.md).
