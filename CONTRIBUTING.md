# Contributing to PoliPage.AspNetCore

Thanks for your interest in improving this package. This guide covers setup, tests, and conventions.

## Setup

```bash
dotnet restore
```

Set `POLI_PAGE_API_KEY` to a `pp_sandbox_…` key (and optionally `POLI_PAGE_BASE_URL`) before running integration tests. The runner reports them as skipped unless the key is a well-formed sandbox key (`pp_sandbox_` plus 32 lowercase hex), so they never spend quota in a counted environment.

## Tests

Unit + WebApplicationFactory tests:

```bash
dotnet test --filter "Category!=Integration"
```

Watch mode:

```bash
dotnet watch --project tests/PoliPage.AspNetCore.Tests test
```

Integration tests (real-API round-trip):

```bash
dotnet test --filter "Category=Integration"
```

Integration tests are reported as skipped unless `POLI_PAGE_API_KEY` is a well-formed sandbox key (`pp_sandbox_` plus 32 lowercase hex).

### Contract tests

`tests/PoliPage.AspNetCore.IntegrationTests/Contract` runs the integration cases (`impl.*`) of the [Poli Page API contract](https://github.com/poli-page/api-contract) against the example app (loaded with `WebApplicationFactory<Program>`), its SDK pointed at a local mock server that answers each case's responses. CI runs them on every leg against the pinned contract (the `poli-page/api-contract` action, pinned by commit SHA). Locally, download a `contract.json` and point `POLI_CONTRACT` at it:

```bash
POLI_CONTRACT=/path/to/contract.json dotnet test tests/PoliPage.AspNetCore.IntegrationTests --filter "Category!=Integration"
```

Without `POLI_CONTRACT` the contract tests are skipped locally; in CI (`CI` set) they fail. `tests/contract-skip.json` maps the case ids allowed to fail to the reason; a listed case that passes fails the run, so remove it once it is fixed.

## Lint, build, pack

```bash
dotnet format --verify-no-changes       # check formatting
dotnet format                           # apply fixes
dotnet build -c Release                 # build
dotnet pack -c Release                  # produce .nupkg
```

CI runs the same commands on `net8.0` + `net10.0` × Ubuntu/Windows + `net10.0` × macOS, with the contract tests.

## Pull requests

- Branch off `main`; open the PR against `main`.
- Keep PRs focused — one feature or fix per PR.
- Add an entry under `## [Unreleased]` in `CHANGELOG.md`.
- Match the existing code style; `dotnet format` will flag drift.
- All public symbols carry XML doc comments — IntelliSense and the auto-generated API reference depend on them.

## Releasing

Nothing publishes without a maintainer's approval. Pushing a `vX.Y.Z` tag runs `.github/workflows/release.yml`: its `build` job checks that the tag matches `<Version>` in `Directory.Build.props` and that `CHANGELOG.md` has a `## [X.Y.Z]` section, then runs format, build, the unit and contract tests (pinned `poli-page/api-contract`) and packs the library; its `publish` job then waits for approval on the `nuget-release` environment, and only on approval pushes the package to nuget.org (Trusted Publishing / OIDC) and creates the GitHub Release from the version's `CHANGELOG.md` section.

The package is [`PoliPage.AspNetCore`](https://www.nuget.org/packages/PoliPage.AspNetCore) on nuget.org. Only the library ships: the `.nupkg` holds `lib/net8.0` and `lib/net10.0` (assembly + XML docs), the README and the package metadata, and the `.snupkg` holds the portable PDBs for the symbol server. The example app and the tests are `IsPackable=false`.

### One-time setup (before the first release)

1. On nuget.org, signed in as the account that owns [`PoliPage`](https://www.nuget.org/packages/PoliPage), open **Trusted Publishing** and add a policy: repository owner `poli-page`, repository `asp`, workflow file `release.yml`, environment `nuget-release`. The policy is not tied to a package ID, so it also covers the first push of `PoliPage.AspNetCore`, which then belongs to that account.
2. In this repo, **Settings → Environments → New environment** `nuget-release`, with **Required reviewers** set to the maintainers who approve releases (and, optionally, deployment tags limited to `v*.*.*`).
3. In this repo, **Settings → Secrets and variables → Actions**, add the repository secret `NUGET_USER`: the nuget.org profile name of the policy owner (not an email), the same value `poli-page/sdk-csharp` uses.

### Per release

1. Bump `<Version>` in `Directory.Build.props`.
2. Move `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD` in `CHANGELOG.md` and fix the compare links at the bottom.
3. If the release breaks the public surface (a minor bump while on 0.x, a major bump from 1.0 on), add a section to `MIGRATION.md`.
4. Merge the release commit to `main`.
5. Run the pre-flight checks locally:
   ```bash
   dotnet format --verify-no-changes
   dotnet build --configuration Release -warnaserror
   POLI_CONTRACT=/path/to/contract.json \
     dotnet test --configuration Release --no-build --filter "Category!=Integration"
   dotnet pack src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj --configuration Release --no-build --output ./nupkg
   ```
6. Inspect the package contents (`unzip -l ./nupkg/PoliPage.AspNetCore.X.Y.Z.nupkg`).
7. Tag the release commit on `main` and push the tag: `git tag vX.Y.Z && git push origin vX.Y.Z`.
8. Approve the `publish` job in the Actions run (environment `nuget-release`). It pushes to nuget.org and creates the GitHub Release.

## Reporting issues

Open an issue in this repo. Include:

- Package version
- ASP.NET Core / .NET version (`dotnet --info`)
- A minimal reproduction (one `Program.cs` is best)
- The relevant `PoliPageException` (if any), including the `RequestId` printed in its message
