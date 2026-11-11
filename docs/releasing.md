# Publishing to NuGet.org

The package is `GtG.Open.HC.SelectionAwarePaging`. Its version is defined in
`Directory.Build.props`; the first release is 16.6.0, matching the minimum
supported Hot Chocolate version.

## One-time account setup

The GitHub `release` environment must allow deployments only from the `main`
branch and contain the variable `NUGET_USER=gtg-open-maintainer`.

Sign in to NuGet.org with the Microsoft account linked to that individual
profile. The profile must be a member of the `gtg-open` NuGet organization.
Create a [Trusted Publishing policy](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
with these values:

| Setting | Value |
| --- | --- |
| Policy owner | `gtg-open` organization |
| GitHub repository owner | `gtg-open` |
| Repository | `selection-aware-paging` |
| Workflow file | `release.yml` |
| Environment | `release` |
| Package glob | `GtG.Open.HC.SelectionAwarePaging` |
| Scope | Push new packages and package versions |

The policy is specific to this repository and package. The DotNetJq policy
does not authorize this workflow. No long-lived NuGet API key is stored in GitHub.

## Release procedure

1. Review the diff, package version, compatibility results and
   [privacy checks](../tools/privacy/README.md). The public README must accurately
   describe the package ID, version and installation procedure.
2. Push the reviewed commit to `main` and verify CI succeeds for that commit.
3. Run **Publish NuGet package** on `main` with `publish=false` for verification
   only, or `publish=true` to publish after all checks pass.
4. The workflow builds and tests the full HC/framework matrix, checks source,
   history and artifacts, and uploads the verified package. The publish job
   downloads that same artifact, checks it again, exchanges the job's GitHub
   identity for a short-lived NuGet key, and pushes the package.
5. Wait for NuGet validation/indexing, verify the package ID, version, owner and
   dependency range, then restore it from NuGet.org in a fresh consumer cache.

Pushes and pull requests run verification only. Publishing requires an explicit
workflow dispatch and the `release` environment's branch restriction. A duplicate
version fails; the workflow never replaces a published NuGet package.
