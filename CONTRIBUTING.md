# Contributing to PoliPage.AspNetCore

Thanks for your interest in improving this package. This guide covers setup, tests, and conventions.

## Setup

```bash
dotnet restore
```

Set `POLI_PAGE_API_KEY` in your environment (a `pp_test_*` key is fine) before running integration tests.

The SDK is not yet on NuGet; the repo's `nuget.config` points at `../sdk-csharp/artifacts/package/release/`. Build the SDK once before working here:

```bash
dotnet pack ../sdk-csharp/PoliPage.sln -c Release -o ../sdk-csharp/artifacts/package/release
```

See [CLAUDE.md §9](CLAUDE.md) for the workaround details.

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

Integration tests are skipped when `POLI_PAGE_API_KEY` is unset.

## Lint, build, pack

```bash
dotnet format --verify-no-changes       # check formatting
dotnet format                           # apply fixes
dotnet build -c Release                 # build
dotnet pack -c Release                  # produce .nupkg
```

CI runs the same commands on `net8.0` + `net10.0` × Ubuntu/Windows + `net10.0` × macOS.

## Pull requests

- Branch off `main`; open the PR against `main`.
- Keep PRs focused — one feature or fix per PR.
- Add an entry under `## [Unreleased]` in `CHANGELOG.md`.
- Match the existing code style; `dotnet format` will flag drift.
- All public symbols carry XML doc comments — IntelliSense and the auto-generated API reference depend on them.

## Reporting issues

Open an issue in this repo. Include:

- Package version
- ASP.NET Core / .NET version (`dotnet --info`)
- A minimal reproduction (one `Program.cs` is best)
- The relevant `PoliPageException` (if any), including the `RequestId` printed in its message
