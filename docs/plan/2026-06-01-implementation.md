# `PoliPage.AspNetCore` v0.1.0 Implementation Plan

> Working plan for the agent picking this up. Each task is one RED → GREEN → refactor slice landing as a single PR. Tasks are sized to be reviewable in under 30 minutes. The order matches `docs/spec/aspnet-core-specification.md` §19 and respects dependencies between slices.

**Author**: Mickael (with Xavier)
**Started**: 2026-06-01
**Status**: pre-flight — repo scaffolded with docs only, no source code yet
**Target**: tag `v0.1.0`, push to NuGet (once `sdk-csharp` has published `1.0.0`)

---

## Amendments applied 2026-06-01 (post-audit)

Driven by `docs/sdk-surface-audit-2026-06-01.md`. The SDK's actual surface diverges from the assumptions baked into the original plan; the changes below are reflected in the affected sections.

1. **Exception classes (Task 12.2 / 12.7)** — renamed to match the SDK: `PoliPageAuthException` (401/403), `PoliPageValidationException` (400/422), `PoliPageNetworkException` (`StatusCode == 0`). Three SDK exceptions the original draft didn't map are now in scope: `PoliPageGoneException` (410), `PoliPagePaymentRequiredException` (402), `PoliPageDownloadException` (storage failure → 502). Constructor shape is `(string code, int statusCode, string message, string? requestId = null, Exception? innerException = null)`. The error-code accessor is `Code`, not `ErrorCode`. `PoliPageRateLimitException.RetryAfter` is now surfaced in ProblemDetails extensions.
2. **Test fixture strategy (Task 13.2–13.5)** — `PoliPageClient`, `Render`, `Documents` are all `sealed` with `internal` constructors, so the original `FakePoliPageClient` subclass cannot exist. Replaced with `StubPoliPageHttpHandler : DelegatingHandler` injected via `PoliPageClientOptions.HttpClient` and `PoliPageClientOptions.DownloadHttpClient`. Most tests don't drive the SDK at all (they construct `PoliPageException` instances directly, or feed `byte[]`/`Stream` into the response helpers), so the ~95% / ~5% split assumed in the spec inverts in practice.
3. **Health check (Task 14, plus 1.3 / 2.1 package references)** — `PoliPageClient.PingAsync` does not exist on the SDK. Task 14 deferred to v0.2; the `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` package references are dropped from Tasks 1.3 and 2.1. CLAUDE.md §2 updated to match. README will document a host-side `IHttpClientFactory`-based fallback probing the smoke endpoint.
4. **Task 0 audit report** — committed as `docs/sdk-surface-audit-2026-06-01.md` (the report ends with a 🟡 Yellow verdict and the precise amendments listed above).

The remaining plan structure is unchanged.

---

## Pre-flight: confirm scaffold is clean

Before starting Task 1, verify the state of the repo:

```bash
cd /Users/mickael/Projects/asp
ls
```

Expect: `README.md`, `CHANGELOG.md`, `CLAUDE.md`, `CONTRIBUTING.md`, `LICENSE`, `docs/`. **No** `src/`, **no** `tests/`, **no** `example-app/`, **no** `.csproj`, **no** `.sln`, **no** `Directory.Build.props`, **no** `nuget.config`. These are produced by the tasks below.

Verify the SDK is buildable:

```bash
dotnet build /Users/mickael/Projects/sdk-csharp/PoliPage.sln -c Release
dotnet pack /Users/mickael/Projects/sdk-csharp/PoliPage.sln -c Release -o /Users/mickael/Projects/sdk-csharp/artifacts/package/release
```

If the SDK is broken or the path differs, stop here and fix `sdk-csharp` first.

Verify the `.env` workspace file at `/Users/mickael/Projects/.env` contains `POLI_PAGE_API_KEY=pp_test_…`. Without it, integration tests will skip silently — fine for CI, frustrating during local development.

---

## Task 0: Verify SDK extension points (PR-less — produces a report, not code)

**Goal**: confirm the three load-bearing SDK assumptions in this plan before any implementation begins. The output is a short markdown report committed at `docs/sdk-surface-audit-2026-06-01.md` (one paragraph per item plus a green/red verdict). No source code changes. If any item lands red, the plan stalls until either the SDK is amended or this plan is.

### 0.1 Exception class names

Check `/Users/mickael/Projects/sdk-csharp/src/PoliPage/Exceptions/`. The mapping switch in spec §10.2 / plan Task 12.2 names six exception classes:

- `PoliPageAuthenticationException` (HTTP 401)
- `PoliPageBadRequestException` (HTTP 400)
- `PoliPageNotFoundException` (HTTP 404)
- `PoliPageRateLimitException` (HTTP 429)
- `PoliPageConnectionException` (no upstream status)
- `PoliPageException` (root)

For each, verify: (a) the class exists with that exact name, (b) the constructor signature accepts `(string message, string? errorCode, string? requestId)` (used throughout the test theories), (c) the class exposes a public `RequestId` property and a `StatusCode` property (used by `PoliPageProblemDetailsFactory.Build`). Record any deltas — different name, different ctor, missing property — in the audit report and amend Task 12.2's mapping `switch` + Task 12.7's test theories before starting Task 12.

### 0.2 `PoliPageClient` extensibility for `FakePoliPageClient`

Open `/Users/mickael/Projects/sdk-csharp/src/PoliPage/PoliPageClient.cs`. The test strategy (spec §14, plan Task 13.2) assumes `FakePoliPageClient` can derive from `PoliPageClient` and override the render methods. Verify:

- `PoliPageClient` is NOT `sealed`.
- The constructor is `public` (or at least `protected`) so a subclass can call it with `pp_test_*` placeholder options.
- `Render.PdfAsync`, `Render.PdfStreamAsync`, `Render.PreviewAsync`, `Documents.GetAsync`, `Documents.CreateAsync`, `Documents.DeleteAsync` are `virtual` (so the fake can override them).

**If `PoliPageClient` is sealed** (or the methods aren't virtual), the test strategy collapses. Two recoveries:

1. **Open a PR against `sdk-csharp` first** to make the client / methods virtual, gated on a `public PoliPageClient(PoliPageClientOptions, …) { … }` constructor. Wait for it to merge before Task 13.
2. **Invert via interface inversion** in the test project: declare `internal interface IFakeablePoliPage { Task<byte[]> RenderPdfAsync(...); … }` in the test assembly, implement it against `PoliPageClient`, fake the interface. Endpoint tests then depend on `IFakeablePoliPage` rather than `PoliPageClient` directly — a much bigger refactor than option 1.

Document the choice in the audit report and amend Task 13.2 accordingly.

### 0.3 `PoliPageClient.PingAsync` for the health check

The health check (plan Task 14.1) calls `client.PingAsync(cancellationToken)`. Verify the method exists on `PoliPageClient` and is cheap (single GET, no body, well under 1 s when the API is healthy).

**If `PingAsync` does not exist**, three options ranked by preference:

1. **Open a PR against `sdk-csharp` first** adding it (`GET /v1/health` or equivalent, returning `204` on healthy). Block Task 14.
2. **Use `Documents.ListAsync(limit: 1)` or another cheap GET** that already exists. Document the choice in Task 14 with a comment naming the trade-off (heavier than a dedicated probe but functional).
3. **Render `getting-started/welcome`** as the probe. Heaviest option; only use if (1) and (2) are both blocked, and open a follow-up against `sdk-csharp` to add a dedicated probe.

### 0.4 SDK CI / build is green on `main`

Run `dotnet build /Users/mickael/Projects/sdk-csharp/PoliPage.sln -c Release` and `dotnet test /Users/mickael/Projects/sdk-csharp -c Release --filter "Category!=Integration"`. Both must pass before any work on the asp repo begins — every CI cell of this plan picks up the SDK as a freshly-packed local source, so a broken SDK breaks every cell of this plan's CI.

### 0.5 Verdict

The audit report ends with one of:

- ✅ **Green — proceed to Task 1.** All six exception classes match; `PoliPageClient` is non-sealed with virtual render methods; `PingAsync` exists; SDK CI is green.
- 🟡 **Yellow — amend the plan first, then proceed.** Deltas exist but recoverable in-plan (e.g., different exception class names → patch Task 12.2's mapping; missing `PingAsync` → patch Task 14.1 to use `Documents.ListAsync(limit: 1)`).
- 🔴 **Red — block until `sdk-csharp` is amended.** `PoliPageClient` is sealed and we want option 0.2(1), not option 0.2(2). Stop, file an SDK issue, wait.

This task takes ~30 minutes. Skipping it can cost a full session of cascading failures.

---

## Task 1: Scaffold the solution, MSBuild props, `nuget.config`, CI workflow

**Goal**: a green CI run on a repo that compiles nothing yet, thanks to the auto-skip step pattern carried from `sdk-csharp`.

**Files to create:**

```
PoliPage.AspNetCore.sln
Directory.Build.props
Directory.Packages.props
global.json
nuget.config
.editorconfig
.gitignore
.github/workflows/ci.yml
```

### 1.1 Solution

```bash
dotnet new sln -n PoliPage.AspNetCore
```

### 1.2 `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>  <!-- silence "missing XML doc" while scaffolding; remove after Task 2 -->

    <!-- AOT + trimming -->
    <IsAotCompatible Condition="'$(TargetFramework)' != 'net8.0'">true</IsAotCompatible>
    <IsTrimmable>true</IsTrimmable>
    <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
  </PropertyGroup>

  <PropertyGroup>
    <Authors>Poli Page</Authors>
    <Company>Poli Page</Company>
    <Copyright>Copyright (c) 2026 Poli Page</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/poli-page/asp</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://github.com/poli-page/asp</PackageProjectUrl>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <DeterministicSourcePaths Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</DeterministicSourcePaths>
  </PropertyGroup>

  <!-- Embed the README in every package so the NuGet listing renders it. -->
  <ItemGroup Condition="'$(IsPackable)' != 'false'">
    <None Include="$(MSBuildThisFileDirectory)README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

`<IsAotCompatible>` is gated to `net10.0` because `net8.0`'s AOT analyzer still flags ASP.NET Core helpers the platform team has since fixed; setting it across the matrix would block CI on a known false-positive. Re-evaluate when net8 reaches EOL in Nov 2026.

### 1.3 `Directory.Packages.props`

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="PoliPage" Version="1.0.0-local" />

    <!-- Analyzers (match sdk-csharp set) -->
    <PackageVersion Include="Meziantou.Analyzer" Version="2.0.183" />
    <PackageVersion Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.0.0" />
    <PackageVersion Include="Roslynator.Analyzers" Version="4.13.1" />

    <!-- Build-time only -->
    <PackageVersion Include="Microsoft.SourceLink.GitHub" Version="8.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />

    <!-- Test dependencies -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.0.1" />
    <PackageVersion Include="FluentAssertions" Version="7.0.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />

    <!-- Per-TFM testing host pins — Mvc.Testing must match the runtime ASP.NET Core major. -->
    <PackageVersion Condition="'$(TargetFramework)' == 'net8.0'"  Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
    <PackageVersion Condition="'$(TargetFramework)' == 'net10.0'" Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />

    <!-- Optional dep (HealthChecks) — deferred to v0.2 (Task 14). Re-add when the SDK ships PingAsync. -->
  </ItemGroup>
</Project>
```

Add to `src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj` (Task 2 patch):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
</ItemGroup>
```

The `Microsoft.Extensions.Configuration.Binder` reference is what activates the binder source generator (the `<EnableConfigurationBindingGenerator>` MSBuild flag in `Directory.Build.props` alone is not enough — the binder package has to be referenced explicitly to ship the analyzer).

### 1.4 `global.json`

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

### 1.5 `nuget.config`

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="poli-page-local" value="../sdk-csharp/artifacts/package/release" />
  </packageSources>
</configuration>
```

### 1.6 `.editorconfig`

Copy from `/Users/mickael/Projects/sdk-csharp/.editorconfig` verbatim. Same conventions, no divergence.

### 1.7 `.gitignore`

Standard .NET ignore — copy from `/Users/mickael/Projects/sdk-csharp/.gitignore` and add ASP.NET-specific entries:

```
# ASP.NET Core
wwwroot/dist/
appsettings.Development.local.json
```

### 1.8 CI workflow

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  test:
    strategy:
      fail-fast: false
      matrix:
        include:
          - { os: ubuntu-latest,  tfm: net8.0 }
          - { os: ubuntu-latest,  tfm: net10.0 }
          - { os: windows-latest, tfm: net8.0 }
          - { os: windows-latest, tfm: net10.0 }
          - { os: macos-latest,   tfm: net10.0 }
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
        with: { path: asp }

      - uses: actions/checkout@v4
        with:
          repository: poli-page/sdk-csharp
          path: sdk-csharp
          ref: main

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x

      - name: Pack sdk-csharp into local source
        run: dotnet pack sdk-csharp/PoliPage.sln -c Release -o sdk-csharp/artifacts/package/release

      - name: Restore
        if: hashFiles('asp/PoliPage.AspNetCore.sln') != ''
        run: dotnet restore asp/PoliPage.AspNetCore.sln

      - name: Format check
        if: hashFiles('asp/.editorconfig') != ''
        run: dotnet format asp/PoliPage.AspNetCore.sln --verify-no-changes

      - name: Build
        if: hashFiles('asp/PoliPage.AspNetCore.sln') != ''
        run: dotnet build asp/PoliPage.AspNetCore.sln -c Release --no-restore -f ${{ matrix.tfm }}

      - name: Test
        if: hashFiles('asp/tests/**/*.csproj') != ''
        run: |
          dotnet test asp/PoliPage.AspNetCore.sln \
            -c Release --no-build \
            -f ${{ matrix.tfm }} \
            --filter "Category!=Integration" \
            --collect "XPlat Code Coverage"

      - name: Pack
        if: hashFiles('asp/src/**/*.csproj') != ''
        run: dotnet pack asp/PoliPage.AspNetCore.sln -c Release --no-build
```

Each step short-circuits via `if: hashFiles(...) != ''` — so an empty repo runs and passes. Adding the manifest in Task 2 lights up the restore + build steps; adding the first test in Task 3 lights up the test step.

### Verification

```bash
git add -A
git commit -m "chore: bootstrap solution scaffold, MSBuild props, CI"
git push
```

The workflow runs across all 5 cells and reports green. Time-to-green should be ~30 seconds per cell.

---

## Task 2: Create the source project skeleton (`PoliPage.AspNetCore.csproj`)

**Goal**: an empty class library that references the SDK and the ASP.NET Core framework, packs to a `.nupkg`, and survives `dotnet format`.

### 2.1 Project file

`src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>PoliPage.AspNetCore</AssemblyName>
    <RootNamespace>PoliPage.AspNetCore</RootNamespace>
    <Description>ASP.NET Core integration for the Poli Page .NET SDK — render PDFs as IResult / IActionResult, IExceptionHandler + middleware mapping PoliPageException to ProblemDetails, smoke endpoint, health checks.</Description>
    <PackageId>PoliPage.AspNetCore</PackageId>
    <PackageTags>polipage;pdf;aspnetcore;minimal-apis;mvc</PackageTags>
    <NoWarn />  <!-- enforce XML docs on every public symbol now -->
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="PoliPage" />
  </ItemGroup>

  <ItemGroup>
    <!-- Runtime: source-gen binder activates only when this package is referenced explicitly. -->
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />

    <!-- HealthChecks integration deferred to v0.2 (Task 14). See docs/sdk-surface-audit-2026-06-01.md §0.3. -->
  </ItemGroup>

  <ItemGroup>
    <!-- Build-time only: SourceLink ships the .pdb→GitHub mapping; analyzers gate quality. -->
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
    <PackageReference Include="Meziantou.Analyzer" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" PrivateAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`PrivateAssets="all"` on SourceLink keeps it out of the consumer's transitive dependency graph — it only contributes the symbol→source mapping in our `.pdb`. The `Microsoft.Extensions.Configuration.Binder` reference is **required at runtime** even though we only use it through the source generator: the generator emits code that calls into the binder's runtime types. Without the package reference, the generated bindings produce `MissingMethodException` at first config bind.

### 2.2 Solution registration

```bash
dotnet sln add src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj
```

### 2.3 Sanity placeholder

`src/PoliPage.AspNetCore/AssemblyMarker.cs`:

```csharp
namespace PoliPage.AspNetCore;

/// <summary>
/// Marker type so the assembly is not empty. Removed once real types land in Task 3.
/// </summary>
internal static class AssemblyMarker
{
}
```

### 2.4 Verification

```bash
dotnet build src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj -c Release
dotnet pack src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj -c Release
```

The build is clean, the pack produces `PoliPage.AspNetCore.0.1.0-local.nupkg` (or similar — version comes from `Directory.Build.props` if you add `<Version>0.1.0-pre</Version>` later). Inspect the `.nupkg` with `unzip -l` and confirm the `PoliPage` reference is in the dependency list and the assembly targets both `net8.0` and `net10.0`.

Push, watch CI light up the restore + build + pack steps.

---

## Task 3: Test project skeleton + first compile-time test

**Goal**: the test step on CI lights up. One trivial passing test proves the runner works; subsequent tasks add real coverage.

### 3.1 Test project

`tests/PoliPage.AspNetCore.Tests/PoliPage.AspNetCore.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
</Project>
```

### 3.2 Solution registration

```bash
dotnet sln add tests/PoliPage.AspNetCore.Tests/PoliPage.AspNetCore.Tests.csproj
```

### 3.3 Global usings

`tests/PoliPage.AspNetCore.Tests/GlobalUsings.cs`:

```csharp
global using FluentAssertions;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Options;
global using PoliPage;
global using PoliPage.AspNetCore;
global using System.Net;
global using System.Net.Http.Json;
global using Xunit;
```

### 3.4 First test

`tests/PoliPage.AspNetCore.Tests/AssemblyMarkerTests.cs`:

```csharp
namespace PoliPage.AspNetCore.Tests;

public class AssemblyMarkerTests
{
    [Fact]
    public void Assembly_loads_and_namespaces_resolve()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.Should().Be("PoliPage.AspNetCore");
    }
}
```

The reference is on `internal` because `[InternalsVisibleTo]` is the next step.

### 3.5 `[InternalsVisibleTo]`

`src/PoliPage.AspNetCore/AssemblyAttributes.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PoliPage.AspNetCore.Tests")]
```

### 3.6 Verification

```bash
dotnet test tests/PoliPage.AspNetCore.Tests/PoliPage.AspNetCore.Tests.csproj
```

One test passes. CI lights up the test step.

---

## Task 4: `PoliPageAspNetCoreOptions` + validator

**Goal**: the integration-specific options bag exists and validates.

**Files:**

- `src/PoliPage.AspNetCore/PoliPageAspNetCoreOptions.cs`
- `src/PoliPage.AspNetCore/Internal/Validators.cs`
- `tests/PoliPage.AspNetCore.Tests/Options/PoliPageAspNetCoreOptionsTests.cs`

### 4.1 RED

`tests/PoliPage.AspNetCore.Tests/Options/PoliPageAspNetCoreOptionsTests.cs`:

```csharp
namespace PoliPage.AspNetCore.Tests.Options;

public class PoliPageAspNetCoreOptionsTests
{
    [Fact]
    public void Defaults_match_documented_values()
    {
        var options = new PoliPageAspNetCoreOptions();

        options.ProblemDetailsTypeUri.Should().Be("https://poli.page/errors");
        options.IncludeRequestIdInProblemDetails.Should().BeTrue();
        options.DefaultCacheControl.Should().Be("no-store, private");
        options.SetNoSniffHeader.Should().BeTrue();
        options.RegisterExceptionHandler.Should().BeTrue();
        options.AddProblemDetailsService.Should().BeTrue();
    }

    [Fact]
    public void RegisterExceptionHandler_disabled_skips_IExceptionHandler_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = "pp_test_x",
            aspnet => aspnet.RegisterExceptionHandler = false);

        services.Any(d =>
            d.ServiceType.FullName == "Microsoft.AspNetCore.Diagnostics.IExceptionHandler"
            && d.ImplementationType?.Name == "PoliPageExceptionHandler")
            .Should().BeFalse();
    }

    [Fact]
    public void RegisterExceptionHandler_enabled_registers_handler()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");
        //                                                ^ RegisterExceptionHandler defaults to true

        services.Any(d => d.ImplementationType?.Name == "PoliPageExceptionHandler")
            .Should().BeTrue();
    }

    [Fact]
    public void AddProblemDetailsService_disabled_skips_AddProblemDetails()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = "pp_test_x",
            aspnet =>
            {
                aspnet.AddProblemDetailsService = false;
                aspnet.RegisterExceptionHandler = false;   // would otherwise pull AddProblemDetails in via cooperation
            });

        services.Any(d => d.ServiceType.FullName == "Microsoft.AspNetCore.Http.IProblemDetailsService")
            .Should().BeFalse();
    }

    [Fact]
    public void AddProblemDetailsService_enabled_registers_problem_details()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        services.Any(d => d.ServiceType.FullName == "Microsoft.AspNetCore.Http.IProblemDetailsService")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/errors/poli")]
    public void Validator_accepts_well_formed_absolute_uris(string uri)
    {
        var options = new PoliPageAspNetCoreOptions { ProblemDetailsTypeUri = uri };
        Internal.Validators.ValidateProblemDetailsTypeUri(options).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("/relative")]
    public void Validator_rejects_malformed_uris(string uri)
    {
        var options = new PoliPageAspNetCoreOptions { ProblemDetailsTypeUri = uri };
        Internal.Validators.ValidateProblemDetailsTypeUri(options).Should().BeFalse();
    }
}
```

Run: red, types don't exist.

### 4.2 GREEN

`src/PoliPage.AspNetCore/PoliPageAspNetCoreOptions.cs`:

```csharp
namespace PoliPage.AspNetCore;

/// <summary>
/// ASP.NET Core-specific options for the Poli Page integration. Augments the SDK's
/// <see cref="PoliPageClientOptions"/> with knobs that only matter inside an HTTP request pipeline.
/// </summary>
public sealed class PoliPageAspNetCoreOptions
{
    /// <summary>
    /// The <c>type</c> URI returned in ProblemDetails responses written by
    /// <c>UsePoliPageExceptionHandler</c>. The exception's <c>ErrorCode</c> is appended as a fragment.
    /// Defaults to <c>https://poli.page/errors</c>.
    /// </summary>
    public string ProblemDetailsTypeUri { get; set; } = "https://poli.page/errors";

    /// <summary>
    /// Whether to include the SDK-provided <see cref="PoliPageException.RequestId"/> in the
    /// ProblemDetails extensions under <c>poliPageRequestId</c>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeRequestIdInProblemDetails { get; set; } = true;

    /// <summary>
    /// Default <c>Cache-Control</c> header value applied by <see cref="PoliPageResults"/>
    /// and <see cref="PoliPageResponseFactory"/>. Defaults to <c>no-store, private</c>.
    /// Set to <see langword="null"/> to omit the header.
    /// </summary>
    public string? DefaultCacheControl { get; set; } = "no-store, private";

    /// <summary>
    /// Whether response helpers add <c>X-Content-Type-Options: nosniff</c>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool SetNoSniffHeader { get; set; } = true;
}
```

`src/PoliPage.AspNetCore/Internal/Validators.cs`:

```csharp
namespace PoliPage.AspNetCore.Internal;

internal static class Validators
{
    public static bool ValidateProblemDetailsTypeUri(PoliPageAspNetCoreOptions options)
        => Uri.TryCreate(options.ProblemDetailsTypeUri, UriKind.Absolute, out _);
}
```

Run: green.

### 4.3 Refactor

Nothing to refactor at this size.

---

## Task 5: `AddPoliPageAspNetCore` — callback overload

**Goal**: the simplest overload registers everything.

**Files:**

- `src/PoliPage.AspNetCore/DependencyInjection/ServiceCollectionExtensions.cs`
- `tests/PoliPage.AspNetCore.Tests/DependencyInjection/AddPoliPageAspNetCoreTests.cs`

### 5.1 RED

```csharp
namespace PoliPage.AspNetCore.Tests.DependencyInjection;

public class AddPoliPageAspNetCoreTests
{
    [Fact]
    public void Registers_PoliPageClient_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<PoliPageClient>();
        var second = provider.GetRequiredService<PoliPageClient>();
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Registers_aspnetcore_options_with_defaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        options.ProblemDetailsTypeUri.Should().Be("https://poli.page/errors");
    }

    [Fact]
    public void Registers_aspnetcore_options_with_overrides()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(
            opts => opts.ApiKey = "pp_test_x",
            aspnet => aspnet.ProblemDetailsTypeUri = "https://example.com/errors");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
        options.ProblemDetailsTypeUri.Should().Be("https://example.com/errors");
    }

    [Fact]
    public void Registers_response_factory_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<PoliPageResponseFactory>();
        var second = provider.GetRequiredService<PoliPageResponseFactory>();
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Double_call_short_circuits_to_a_single_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        services.Count(d => d.ServiceType == typeof(PoliPageResponseFactory)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(PoliPageClient)).Should().Be(1);
    }

    [Fact]
    public void Sdk_add_then_aspnetcore_short_circuits()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPoliPage(opts => opts.ApiKey = "pp_test_x");        // user calls SDK first
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        services.Count(d => d.ServiceType == typeof(PoliPageClient)).Should().Be(1);
    }
}
```

### 5.2 GREEN

`src/PoliPage.AspNetCore/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PoliPage.AspNetCore.Internal;
using PoliPage.AspNetCore.Middleware;

namespace PoliPage.AspNetCore;

/// <summary>
/// Extensions for registering Poli Page ASP.NET Core services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Poli Page SDK and ASP.NET Core integration services using callback configuration.
    /// </summary>
    public static IServiceCollection AddPoliPageAspNetCore(
        this IServiceCollection services,
        Action<PoliPageClientOptions> configureClient,
        Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureClient);

        services.AddPoliPage(configureClient);
        return AddAspNetCoreServices(services, configureAspNetCore);
    }

    private static IServiceCollection AddAspNetCoreServices(
        IServiceCollection services,
        Action<PoliPageAspNetCoreOptions>? configure)
    {
        // Marker-gated short-circuit. See spec §7.5 — guards against:
        //   (a) the same AddPoliPageAspNetCore() call made twice by composed extension methods, and
        //   (b) a user calling services.AddPoliPage(...) first, then services.AddPoliPageAspNetCore(...).
        // Does NOT guard the reverse order — see CLAUDE.md §10.14.
        if (services.Any(d => d.ServiceType == typeof(PoliPageResponseFactory)))
            return services;

        // Snapshot the ASP.NET-side flags eagerly so RegisterExceptionHandler / AddProblemDetailsService
        // are knowable at registration time (they gate Add*() calls below).
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);

        services.AddOptions<PoliPageAspNetCoreOptions>()
            .Configure(o =>
            {
                o.ProblemDetailsTypeUri = aspnet.ProblemDetailsTypeUri;
                o.IncludeRequestIdInProblemDetails = aspnet.IncludeRequestIdInProblemDetails;
                o.DefaultCacheControl = aspnet.DefaultCacheControl;
                o.SetNoSniffHeader = aspnet.SetNoSniffHeader;
                o.RegisterExceptionHandler = aspnet.RegisterExceptionHandler;
                o.AddProblemDetailsService = aspnet.AddProblemDetailsService;
            })
            .Validate(Validators.ValidateProblemDetailsTypeUri,
                "PoliPage.AspNetCore: ProblemDetailsTypeUri must be a well-formed absolute URI.")
            .ValidateOnStart();

        services.AddSingleton<PoliPageResponseFactory>();
        services.AddSingleton<ExceptionHandling.PoliPageProblemDetailsFactory>();

        if (aspnet.AddProblemDetailsService)
            services.AddProblemDetails();

        if (aspnet.RegisterExceptionHandler)
            services.AddExceptionHandler<ExceptionHandling.PoliPageExceptionHandler>();

        return services;
    }
}
```

`src/PoliPage.AspNetCore/Mvc/PoliPageResponseFactory.cs` — minimal stub for now (filled in Task 9):

```csharp
namespace PoliPage.AspNetCore;

public sealed class PoliPageResponseFactory
{
    private readonly IOptions<PoliPageAspNetCoreOptions> _options;
    public PoliPageResponseFactory(IOptions<PoliPageAspNetCoreOptions> options) => _options = options;
}
```

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageProblemDetailsFactory.cs` — minimal stub (filled in Task 12):

```csharp
namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageProblemDetailsFactory
{
    private readonly IOptions<PoliPageAspNetCoreOptions> _options;
    public PoliPageProblemDetailsFactory(IOptions<PoliPageAspNetCoreOptions> options) => _options = options;
}
```

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageExceptionHandler.cs` — minimal stub so `services.AddExceptionHandler<PoliPageExceptionHandler>()` resolves at registration time (real implementation lands in Task 12):

```csharp
namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext _, Exception __, CancellationToken ___)
        => ValueTask.FromResult(false);  // filled in Task 12
}
```

Run: green.

### 5.3 Refactor

Verify the extension method signature surfaces in IntelliSense from a `WebApplicationBuilder.Services` (it does, because `IServiceCollection` is the receiver — no extra plumbing needed).

---

## Task 6: `AddPoliPageAspNetCore` — `IConfiguration` overloads

**Goal**: the two configuration-bound overloads bind from a `IConfigurationSection`.

### 6.1 RED

```csharp
[Fact]
public void Binds_client_options_from_configuration_section()
{
    var inMemoryConfig = new Dictionary<string, string?>
    {
        ["PoliPage:ApiKey"] = "pp_test_bound",
        ["PoliPage:MaxRetries"] = "5",
        ["PoliPage:RequestTimeout"] = "00:00:30",
    };
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(inMemoryConfig)
        .Build();

    var services = new ServiceCollection();
    services.AddLogging();

    services.AddPoliPageAspNetCore(configuration.GetSection("PoliPage"));

    using var provider = services.BuildServiceProvider();
    var clientOptions = provider.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
    clientOptions.ApiKey.Should().Be("pp_test_bound");
    clientOptions.MaxRetries.Should().Be(5);
    clientOptions.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
}

[Fact]
public void Binds_aspnetcore_options_from_aspnetcore_subsection()
{
    var inMemoryConfig = new Dictionary<string, string?>
    {
        ["PoliPage:ApiKey"] = "pp_test_x",
        ["PoliPage:AspNetCore:ProblemDetailsTypeUri"] = "https://example.com/errors",
        ["PoliPage:AspNetCore:SetNoSniffHeader"] = "false",
    };
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(inMemoryConfig)
        .Build();

    var services = new ServiceCollection();
    services.AddLogging();

    services.AddPoliPageAspNetCore(configuration.GetSection("PoliPage"));

    using var provider = services.BuildServiceProvider();
    var aspnetOptions = provider.GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;
    aspnetOptions.ProblemDetailsTypeUri.Should().Be("https://example.com/errors");
    aspnetOptions.SetNoSniffHeader.Should().BeFalse();
}
```

### 6.2 GREEN

Add the two overloads to `ServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddPoliPageAspNetCore(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);

    services.AddPoliPage(opts => configuration.Bind(opts));
    return AddAspNetCoreServices(
        services,
        aspNet =>
        {
            configuration.GetSection("AspNetCore").Bind(aspNet);
            configureAspNetCore?.Invoke(aspNet);
        });
}

public static IServiceCollection AddPoliPageAspNetCore(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<PoliPageClientOptions> configureClient,
    Action<PoliPageAspNetCoreOptions>? configureAspNetCore = null)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(configureClient);

    services.AddPoliPage(opts =>
    {
        configuration.Bind(opts);
        configureClient(opts);
    });
    return AddAspNetCoreServices(
        services,
        aspNet =>
        {
            configuration.GetSection("AspNetCore").Bind(aspNet);
            configureAspNetCore?.Invoke(aspNet);
        });
}
```

Run: green.

### 6.3 Refactor

`configuration.Bind(opts)` accepts `TimeSpan` strings (`"00:00:30"`) out of the box via the default `BindingSource`. Verify with the `RequestTimeout` assertion above — the test passes with no extra binder registration needed.

---

## Task 7: `ValidateOnStart` smoke test

**Goal**: a misconfigured host fails fast.

### 7.1 RED + GREEN combined

```csharp
[Fact]
public async Task ValidateOnStart_throws_on_missing_api_key()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddPoliPageAspNetCore(opts => opts.ApiKey = string.Empty);

    using var provider = services.BuildServiceProvider();
    var validator = provider.GetRequiredService<IStartupValidator>();

    Action act = () => validator.Validate();
    act.Should().Throw<OptionsValidationException>().WithMessage("*ApiKey*required*");
}

[Fact]
public async Task ValidateOnStart_throws_on_bad_problem_details_uri()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddPoliPageAspNetCore(
        opts => opts.ApiKey = "pp_test_x",
        aspnet => aspnet.ProblemDetailsTypeUri = "not-a-uri");

    using var provider = services.BuildServiceProvider();
    var validator = provider.GetRequiredService<IStartupValidator>();

    Action act = () => validator.Validate();
    act.Should().Throw<OptionsValidationException>().WithMessage("*ProblemDetailsTypeUri*");
}
```

Should pass on first run — `ValidateOnStart` is already wired in `AddAspNetCoreServices`. If a test fails here, the SDK's validator messages may have changed; update the expected message regex.

---

## Task 8: `ContentDispositionHeader` + tests

**Goal**: RFC 5987 filename encoding has a single, tested implementation.

**File**: `src/PoliPage.AspNetCore/Mvc/ContentDispositionHeader.cs` — implementation per spec §8.4.

**Tests**: `tests/PoliPage.AspNetCore.Tests/Mvc/ContentDispositionHeaderTests.cs`:

```csharp
public class ContentDispositionHeaderTests
{
    [Fact]
    public void ASCII_filename_produces_basic_form()
    {
        ContentDispositionHeader.Build("invoice.pdf", inline: false)
            .Should().Be("attachment; filename=\"invoice.pdf\"");
    }

    [Fact]
    public void Inline_flag_swaps_attachment_for_inline()
    {
        ContentDispositionHeader.Build("invoice.pdf", inline: true)
            .Should().Be("inline; filename=\"invoice.pdf\"");
    }

    [Fact]
    public void Embedded_quote_is_backslash_escaped()
    {
        ContentDispositionHeader.Build("invoice\"of-doom.pdf", inline: false)
            .Should().Be("attachment; filename=\"invoice\\\"of-doom.pdf\"");
    }

    [Fact]
    public void Non_ASCII_filename_produces_dual_form()
    {
        var actual = ContentDispositionHeader.Build("facture-été-2026.pdf", inline: false);
        actual.Should().StartWith("attachment; filename=\"facture-___-2026.pdf\"");
        actual.Should().Contain("filename*=UTF-8''facture-%C3%A9t%C3%A9-2026.pdf");
    }

    [Fact]
    public void Empty_filename_throws()
    {
        Action act = () => ContentDispositionHeader.Build(string.Empty, inline: false);
        act.Should().Throw<ArgumentException>().WithParameterName("filename");
    }
}
```

Mirror the algorithm from `nextjs/src/responses/headers.ts` and `symfony-bundle/src/Http/PoliPageResponseFactory.php::makeDisposition()`. Reuse the test cases from `nextjs/tests/unit/headers.test.ts`.

---

## Task 9: `PoliPageResults.Pdf` + `PdfResult`

**Goal**: a Minimal API endpoint returns a PDF with correct headers.

### 9.1 Implementation

`src/PoliPage.AspNetCore/Results/PdfResult.cs`:

```csharp
namespace PoliPage.AspNetCore.Results;

internal sealed class PdfResult(byte[] pdf, string? filename, bool inline)
    : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "application/pdf";
        httpContext.Response.ContentLength = pdf.Length;

        if (filename is not null)
            httpContext.Response.Headers.ContentDisposition =
                ContentDispositionHeader.Build(filename, inline);

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await httpContext.Response.Body.WriteAsync(pdf, httpContext.RequestAborted);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status200OK,
            type: typeof(byte[]),
            contentTypes: ["application/pdf"]));
    }
}
```

Add to the existing test class:

```csharp
[Fact]
public void Populates_openapi_metadata_for_200_application_pdf()
{
    var endpoints = new TestEndpointRouteBuilder();
    endpoints.MapGet("/test", () => PoliPageResults.Pdf("%PDF-"u8.ToArray()));
    var endpoint = endpoints.DataSources.SelectMany(s => s.Endpoints).Single();

    var metadata = endpoint.Metadata
        .OfType<IProducesResponseTypeMetadata>()
        .Should().ContainSingle(m => m.StatusCode == 200).Subject;

    metadata.ContentTypes.Should().Contain("application/pdf");
}
```

`TestEndpointRouteBuilder` is a minimal `IEndpointRouteBuilder` impl in the test fixtures folder — three lines wrapping an `EndpointDataSource` list. The exact shape ships in `tests/PoliPage.AspNetCore.Tests/Fixtures/TestEndpointRouteBuilder.cs`.

`src/PoliPage.AspNetCore/Results/PoliPageResults.cs` (start with `Pdf` only; the other three methods land in Task 10):

```csharp
namespace PoliPage.AspNetCore;

public static class PoliPageResults
{
    /// <summary>
    /// Returns the rendered PDF bytes with <c>application/pdf</c>, RFC 5987-encoded
    /// <c>Content-Disposition</c>, and the configured cache + nosniff headers.
    /// </summary>
    public static IResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return new PdfResult(pdf, filename, inline);
    }
}
```

### 9.2 Tests

`tests/PoliPage.AspNetCore.Tests/Results/PdfResultTests.cs`:

```csharp
public class PdfResultTests
{
    [Fact]
    public async Task Writes_pdf_with_default_headers()
    {
        var pdf = "%PDF-1.7\n%fake"u8.ToArray();
        var (httpContext, body) = CreateContextWithDefaultOptions();

        await PoliPageResults.Pdf(pdf, "invoice.pdf").ExecuteAsync(httpContext);

        httpContext.Response.ContentType.Should().Be("application/pdf");
        httpContext.Response.Headers.ContentDisposition.ToString()
            .Should().Be("attachment; filename=\"invoice.pdf\"");
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store, private");
        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        body.ToArray().Should().Equal(pdf);
    }

    [Fact]
    public async Task Omits_disposition_when_filename_null()
    {
        var pdf = "%PDF-fake"u8.ToArray();
        var (httpContext, body) = CreateContextWithDefaultOptions();

        await PoliPageResults.Pdf(pdf).ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("Content-Disposition");
    }

    [Fact]
    public async Task Omits_cache_control_when_default_null()
    {
        var pdf = "%PDF-fake"u8.ToArray();
        var (httpContext, _) = CreateContext(opts => opts.DefaultCacheControl = null);

        await PoliPageResults.Pdf(pdf, "x.pdf").ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("Cache-Control");
    }

    [Fact]
    public async Task Omits_nosniff_when_disabled()
    {
        var pdf = "%PDF-fake"u8.ToArray();
        var (httpContext, _) = CreateContext(opts => opts.SetNoSniffHeader = false);

        await PoliPageResults.Pdf(pdf, "x.pdf").ExecuteAsync(httpContext);

        httpContext.Response.Headers.Should().NotContainKey("X-Content-Type-Options");
    }

    private static (DefaultHttpContext context, MemoryStream body) CreateContext(
        Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x", configure);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        return (httpContext, body);
    }

    private static (DefaultHttpContext context, MemoryStream body) CreateContextWithDefaultOptions()
        => CreateContext();
}
```

Five tests. RED, GREEN, refactor — running tests after each.

---

## Task 10: `PoliPageResults.PdfStream`, `Preview`, `DocumentRedirect`

**Goal**: the remaining three Minimal API helpers.

### 10.1 `PdfStreamResult`

```csharp
internal sealed class PdfStreamResult(Stream pdfStream, string? filename, bool inline)
    : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        await using var stream = pdfStream;  // take ownership
        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "application/pdf";

        if (filename is not null)
            httpContext.Response.Headers.ContentDisposition =
                ContentDispositionHeader.Build(filename, inline);

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status200OK,
            type: typeof(Stream),
            contentTypes: ["application/pdf"]));
    }
}
```

Tests: assert headers + assert `Response.Body` contains the source stream's bytes after `ExecuteAsync` + assert OpenAPI metadata (200, `application/pdf`). Use a `MemoryStream` as the source and a `MemoryStream` as the response body.

### 10.2 `PreviewResult`

```csharp
internal sealed class PreviewResult(string html)
    : IResult, IEndpointMetadataProvider
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PoliPageAspNetCoreOptions>>().Value;

        httpContext.Response.ContentType = "text/html; charset=utf-8";

        if (options.DefaultCacheControl is not null)
            httpContext.Response.Headers.CacheControl = options.DefaultCacheControl;

        if (options.SetNoSniffHeader)
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        await httpContext.Response.WriteAsync(html, httpContext.RequestAborted);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status200OK,
            type: typeof(string),
            contentTypes: ["text/html; charset=utf-8"]));
    }
}
```

Tests: assert content-type, headers, body + OpenAPI metadata. Empty string passes through (don't throw — empty HTML is a valid render result for some templates).

### 10.3 `DocumentRedirectResult`

```csharp
internal sealed class DocumentRedirectResult(string presignedUrl)
    : IResult, IEndpointMetadataProvider
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status302Found;
        httpContext.Response.Headers.Location = presignedUrl;
        return Task.CompletedTask;
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode: StatusCodes.Status302Found,
            type: typeof(void),
            contentTypes: []));
    }
}
```

Tests: assert status + Location header + OpenAPI metadata (302). Argument null check on `presignedUrl`.

### 10.4 Add the three remaining methods to `PoliPageResults`

```csharp
public static IResult PdfStream(Stream pdfStream, string? filename = null, bool inline = false)
{
    ArgumentNullException.ThrowIfNull(pdfStream);
    return new Results.PdfStreamResult(pdfStream, filename, inline);
}

public static IResult Preview(string html)
{
    ArgumentNullException.ThrowIfNull(html);
    return new Results.PreviewResult(html);
}

public static IResult DocumentRedirect(string presignedUrl)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(presignedUrl);
    return new Results.DocumentRedirectResult(presignedUrl);
}
```

---

## Task 11: `PoliPageResponseFactory` (MVC) + tests

**Goal**: the MVC-side surface.

### 11.1 Implementation

`src/PoliPage.AspNetCore/Mvc/PoliPageResponseFactory.cs`:

```csharp
namespace PoliPage.AspNetCore;

public sealed class PoliPageResponseFactory(IOptions<PoliPageAspNetCoreOptions> options)
{
    private readonly PoliPageAspNetCoreOptions _options = options.Value;

    public FileContentResult Pdf(byte[] pdf, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return new FileContentResult(pdf, "application/pdf")
        {
            FileDownloadName = inline ? null : filename,
        };
    }

    public FileStreamResult PdfStream(Stream pdf, string? filename = null, bool inline = false)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return new FileStreamResult(pdf, "application/pdf")
        {
            FileDownloadName = inline ? null : filename,
        };
    }

    public ContentResult Preview(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
    }

    public RedirectResult DocumentRedirect(string presignedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presignedUrl);
        return new RedirectResult(presignedUrl, permanent: false);
    }
}
```

For RFC 5987 encoding on the MVC path, the README and `docs/responses.md` document the explicit header-writing pattern. We keep `PoliPageResponseFactory` thin to avoid duplicating the `IActionFilter` plumbing that MVC's own `FileContentResult` does for the simple ASCII case.

### 11.2 Tests

End-to-end via `WebApplicationFactory<Program>` — easier than running the action result against a fake `ActionContext`. Defer the WebApplicationFactory fixture to Task 13; for now, unit-test the factory's return-value shapes.

```csharp
public class PoliPageResponseFactoryTests
{
    private static PoliPageResponseFactory CreateFactory(Action<PoliPageAspNetCoreOptions>? configure = null)
    {
        var aspnet = new PoliPageAspNetCoreOptions();
        configure?.Invoke(aspnet);
        return new PoliPageResponseFactory(Options.Create(aspnet));
    }

    [Fact]
    public void Pdf_returns_FileContentResult_with_filename()
    {
        var factory = CreateFactory();
        var result = factory.Pdf("%PDF-x"u8.ToArray(), "invoice.pdf");
        result.ContentType.Should().Be("application/pdf");
        result.FileDownloadName.Should().Be("invoice.pdf");
    }

    [Fact]
    public void Pdf_with_inline_true_clears_FileDownloadName()
    {
        var result = CreateFactory().Pdf("%PDF-x"u8.ToArray(), "invoice.pdf", inline: true);
        result.FileDownloadName.Should().BeEmpty();   // ASP.NET Core treats null/empty equivalently
    }

    [Fact]
    public void Preview_returns_html_content_result()
    {
        var result = CreateFactory().Preview("<h1>x</h1>");
        result.Content.Should().Be("<h1>x</h1>");
        result.ContentType.Should().Be("text/html; charset=utf-8");
    }

    [Fact]
    public void DocumentRedirect_returns_temporary_redirect()
    {
        var result = CreateFactory().DocumentRedirect("https://example.com/doc.pdf");
        result.Url.Should().Be("https://example.com/doc.pdf");
        result.Permanent.Should().BeFalse();
    }
}
```

---

## Task 12: Exception handling — `IExceptionHandler` (primary) + middleware (fallback) + `ProblemDetails` mapping

**Goal**: `PoliPageException` maps to RFC 7807 `ProblemDetails` via both the .NET 8+ `IExceptionHandler` path (auto-registered by `AddPoliPageAspNetCore`) and the legacy `IApplicationBuilder.UsePoliPageExceptionHandler()` middleware (fallback for hosts that don't call `app.UseExceptionHandler()`). Both delegate the final write to `IProblemDetailsService` so any `services.AddProblemDetails(opts => opts.CustomizeProblemDetails = ...)` callbacks the user registered also run on our responses.

### 12.1 `LogMessages` source-gen extension methods

Define every log entry as a `[LoggerMessage]` partial method **before** writing any logger callsite — analyzer `CA1848` is on and will fail the build otherwise.

`src/PoliPage.AspNetCore/Internal/LogMessages.cs`:

```csharp
namespace PoliPage.AspNetCore.Internal;

internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "PoliPageException thrown after response started; cannot rewrite headers.")]
    public static partial void ExceptionAfterResponseStarted(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "MapPoliPageSmokeTest is registered without explicit .RequireAuthorization(...) or .AllowAnonymous(). " +
                  "In production this endpoint will burn API quota on unauthenticated callers — gate it or opt out explicitly.")]
    public static partial void SmokeEndpointUnguarded(ILogger logger);
}
```

### 12.2 `PoliPageProblemDetailsFactory`

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageProblemDetailsFactory.cs`:

```csharp
internal sealed class PoliPageProblemDetailsFactory(IOptions<PoliPageAspNetCoreOptions> options)
{
    private readonly PoliPageAspNetCoreOptions _options = options.Value;

    public ProblemDetails Build(HttpContext httpContext, PoliPageException exception)
    {
        var (status, code, title) = Map(exception);

        var problem = new ProblemDetails
        {
            Type = $"{_options.ProblemDetailsTypeUri}#{code}",
            Title = title,
            Status = status,
            Detail = exception.Message,
            Instance = httpContext.Request.Path + httpContext.Request.QueryString,
        };

        problem.Extensions["code"] = code;
        if (_options.IncludeRequestIdInProblemDetails && exception.RequestId is { } requestId)
            problem.Extensions["poliPageRequestId"] = requestId;

        if (exception is PoliPageRateLimitException { RetryAfter: { } retryAfter })
            problem.Extensions["retryAfterSeconds"] = (int)Math.Ceiling(retryAfter.TotalSeconds);

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        if (!string.IsNullOrEmpty(traceId))
            problem.Extensions["traceId"] = traceId;

        return problem;
    }

    // Status / problem-code / title resolution. Confirmed against
    // /Users/mickael/Projects/sdk-csharp/src/PoliPage/Exceptions/ on 2026-06-01
    // (see docs/sdk-surface-audit-2026-06-01.md §0.1). `exception.Code` is the
    // wire-level error code (e.g. "VALIDATION") — keep it on the ProblemDetails
    // extensions under "code"; the second tuple slot here is the *problem-code*
    // label that we expose in the public ProblemDetails Type URI fragment.
    private static (int status, string code, string title) Map(PoliPageException exception)
        => exception switch
        {
            PoliPageAuthException             => (StatusCodes.Status401Unauthorized,        "authentication_failed",  "Authentication failed"),
            PoliPagePaymentRequiredException  => (StatusCodes.Status402PaymentRequired,     "payment_required",       "Payment required"),
            PoliPageNotFoundException         => (StatusCodes.Status404NotFound,            "not_found",              "Not found"),
            PoliPageGoneException             => (StatusCodes.Status410Gone,                "gone",                   "Resource permanently gone"),
            PoliPageValidationException ex    => (ex.StatusCode == 400
                                                      ? StatusCodes.Status400BadRequest
                                                      : StatusCodes.Status422UnprocessableEntity,
                                                  "validation_failed",      "Validation failed"),
            PoliPageRateLimitException        => (StatusCodes.Status429TooManyRequests,     "rate_limited",           "Rate limit exceeded"),
            PoliPageNetworkException          => (StatusCodes.Status502BadGateway,          "upstream_unavailable",   "Upstream unavailable"),
            PoliPageDownloadException         => (StatusCodes.Status502BadGateway,          "download_failed",        "Stored document download failed"),
            _                                 => (StatusCodes.Status500InternalServerError, "poli_page_error",        "Poli Page error"),
        };
}
```

The default branch is now 500 (not 502) so a generic `PoliPageException` doesn't claim the upstream is at fault — that label belongs only to the network + download cases. The Map is hard-locked against the SDK's actual exception classes as of `sdk-csharp@d74ec9f` — re-check if the SDK adds new types.

### 12.3 `PoliPageExceptionHandler` — the primary path (`IExceptionHandler`)

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageExceptionHandler.cs`:

```csharp
namespace PoliPage.AspNetCore.ExceptionHandling;

internal sealed class PoliPageExceptionHandler(
    PoliPageProblemDetailsFactory factory,
    IProblemDetailsService problemDetailsService,
    ILogger<PoliPageExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PoliPageException poliEx) return false;

        if (httpContext.Response.HasStarted)
        {
            LogMessages.ExceptionAfterResponseStarted(logger, poliEx);
            return false;
        }

        var problem = factory.Build(httpContext, poliEx);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        Activity.Current?.SetStatus(ActivityStatusCode.Error, poliEx.Message);
        Activity.Current?.AddTag("polipage.error.code", problem.Extensions["code"]);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
```

Auto-registration is wired in `AddAspNetCoreServices` (Task 5) — when `PoliPageAspNetCoreOptions.RegisterExceptionHandler == true`:

```csharp
if (aspnet.RegisterExceptionHandler)
    services.AddExceptionHandler<PoliPageExceptionHandler>();
if (aspnet.AddProblemDetailsService)
    services.AddProblemDetails();
```

### 12.4 `PoliPageExceptionHandlerMiddleware` — the fallback

`src/PoliPage.AspNetCore/ExceptionHandling/PoliPageExceptionHandlerMiddleware.cs`:

```csharp
internal sealed class PoliPageExceptionHandlerMiddleware(
    RequestDelegate next,
    PoliPageProblemDetailsFactory factory,
    IProblemDetailsService problemDetailsService,
    ILogger<PoliPageExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await next(httpContext);
        }
        catch (PoliPageException ex)
        {
            if (httpContext.Response.HasStarted)
            {
                LogMessages.ExceptionAfterResponseStarted(logger, ex);
                throw;
            }

            var problem = factory.Build(httpContext, ex);
            httpContext.Response.Clear();
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = ex,
            });

            if (!written)
            {
                httpContext.Response.ContentType = "application/problem+json";
                await httpContext.Response.WriteAsJsonAsync(
                    problem,
                    ProblemDetailsJsonContext.Default.ProblemDetails,
                    contentType: "application/problem+json",
                    cancellationToken: httpContext.RequestAborted);
            }
        }
    }
}
```

### 12.5 JSON source-gen context

`src/PoliPage.AspNetCore/ExceptionHandling/ProblemDetailsJsonContext.cs`:

```csharp
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class ProblemDetailsJsonContext : JsonSerializerContext
{
}
```

### 12.6 `UsePoliPageExceptionHandler` extension

`src/PoliPage.AspNetCore/ExceptionHandling/ApplicationBuilderExtensions.cs`:

```csharp
namespace PoliPage.AspNetCore;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the Poli Page exception handler middleware as a fallback for hosts that do
    /// not call <see cref="ExceptionHandlerExtensions.UseExceptionHandler"/>. .NET 8+ hosts
    /// using <c>app.UseExceptionHandler()</c> already get the primary
    /// <see cref="IExceptionHandler"/> path and do NOT need this call.
    /// </summary>
    public static IApplicationBuilder UsePoliPageExceptionHandler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ExceptionHandling.PoliPageExceptionHandlerMiddleware>();
    }
}
```

### 12.7 Tests — primary path (`IExceptionHandler`)

`tests/PoliPage.AspNetCore.Tests/ExceptionHandling/PoliPageExceptionHandlerTests.cs`:

```csharp
public class PoliPageExceptionHandlerTests : IClassFixture<PoliPageWebApplicationFactory>
{
    private readonly PoliPageWebApplicationFactory _factory;
    public PoliPageExceptionHandlerTests(PoliPageWebApplicationFactory factory) => _factory = factory;

    // Each row drives one exception type through the handler. The fixture's
    // factory delegate constructs the right ctor shape for each — see Map_factory
    // below — because the SDK's PoliPageException-derived ctors take
    // (code, statusCode, message, requestId?, innerException?).
    public static TheoryData<Func<PoliPageException>, HttpStatusCode, string> Map_factory => new()
    {
        { () => new PoliPageAuthException(PoliPageErrorCode.Unauthorized, 401, "Bad key", requestId: "req_x"),
          HttpStatusCode.Unauthorized,        "authentication_failed" },
        { () => new PoliPagePaymentRequiredException(PoliPageErrorCode.PaymentRequired, 402, "Owed", requestId: "req_x"),
          HttpStatusCode.PaymentRequired,     "payment_required" },
        { () => new PoliPageNotFoundException(PoliPageErrorCode.NotFound, 404, "Missing", requestId: "req_x"),
          HttpStatusCode.NotFound,            "not_found" },
        { () => new PoliPageGoneException(PoliPageErrorCode.Gone, 410, "Gone", requestId: "req_x"),
          HttpStatusCode.Gone,                "gone" },
        { () => new PoliPageValidationException(PoliPageErrorCode.Validation, 400, "Bad input", requestId: "req_x"),
          HttpStatusCode.BadRequest,          "validation_failed" },
        { () => new PoliPageValidationException(PoliPageErrorCode.Validation, 422, "Schema fail", requestId: "req_x"),
          HttpStatusCode.UnprocessableEntity, "validation_failed" },
        { () => new PoliPageRateLimitException(PoliPageErrorCode.RateLimit, 429, "Slow down",
                                               requestId: "req_x", retryAfter: TimeSpan.FromSeconds(7)),
          HttpStatusCode.TooManyRequests,     "rate_limited" },
        { () => new PoliPageNetworkException(PoliPageErrorCode.Network, "DNS", new HttpRequestException("dns")),
          HttpStatusCode.BadGateway,          "upstream_unavailable" },
        { () => new PoliPageDownloadException(PoliPageErrorCode.DownloadFailed, 0, "S3 503", requestId: "req_x"),
          HttpStatusCode.BadGateway,          "download_failed" },
    };

    [Theory]
    [MemberData(nameof(Map_factory))]
    public async Task Maps_to_status_and_code(
        Func<PoliPageException> exFactory,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var ex = exFactory();
        _factory.Stub.SetException(ex);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(expectedCode);

        // RequestId not set on PoliPageNetworkException (the SDK omits it by design).
        if (ex.RequestId is not null)
            problem.GetProperty("poliPageRequestId").GetString().Should().Be(ex.RequestId);

        // RetryAfter surfaces for rate-limit responses only.
        if (ex is PoliPageRateLimitException { RetryAfter: { } retry })
            problem.GetProperty("retryAfterSeconds").GetInt32().Should().Be((int)retry.TotalSeconds);
    }

    [Fact]
    public async Task Non_PoliPageException_falls_through_to_default_500()
    {
        _factory.Fake.SetException(new InvalidOperationException("not ours"));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        // Default 500 page from ASP.NET Core; not our ProblemDetails shape.
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/problem+json");
    }

    [Fact]
    public async Task Sets_activity_status_to_Error_on_caught_PoliPageException()
    {
        using var source = new ActivitySource("PoliPage.AspNetCore.Tests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var captured = new List<ActivityStatusCode>();
        listener.ActivityStopped = a => captured.Add(a.Status);

        _factory.Fake.SetException(new PoliPageBadRequestException("invalid template", null, "req_y"));
        using var client = _factory.CreateClient();
        _ = await client.GetAsync("/poli-page/smoke");

        captured.Should().Contain(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Cooperates_with_CustomizeProblemDetails_callback()
    {
        await using var factory = new PoliPageWebApplicationFactory(configureServices: services =>
        {
            services.AddProblemDetails(opts =>
                opts.CustomizeProblemDetails = ctx =>
                    ctx.ProblemDetails.Extensions["customField"] = "custom-value");
        });
        factory.Fake.SetException(new PoliPageBadRequestException("x", null, "req_z"));

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("customField").GetString().Should().Be("custom-value");
    }
}
```

### 12.8 Tests — fallback path (middleware)

`tests/PoliPage.AspNetCore.Tests/ExceptionHandling/PoliPageExceptionHandlerMiddlewareTests.cs`:

A second `WebApplicationFactory<TFallbackProgram>` boots a host without `UseExceptionHandler()` and with `RegisterExceptionHandler = false` + `app.UsePoliPageExceptionHandler()`. Same Theory matrix as 12.7 — proves the fallback path matches.

Plus:

```csharp
[Fact]
public async Task Falls_back_to_source_gen_writer_when_no_AddProblemDetails()
{
    // Boot host with AddProblemDetailsService = false; the middleware's TryWriteAsync
    // returns false; the fallback `WriteAsJsonAsync` writes the source-gen JSON.
    /* … */
}
```

### 12.9 Test count target

At least 12 tests across the two suites:
- 5 status-mapping cases (Theory) — primary path
- 5 status-mapping cases (Theory) — fallback path
- 1 non-`PoliPageException` falls through (primary)
- 1 activity status set
- 1 `CustomizeProblemDetails` cooperation
- 1 source-gen fallback path
- 1 response-has-started rethrow (primary AND fallback — share the test class via a shared `[Theory]`)

---

## Task 13: `MapPoliPageSmokeTest` endpoint (with auth-guard warning) + WebApplicationFactory fixtures

**Goal**: a smoke endpoint that renders the well-known template, emits a startup warning when registered without an explicit auth decision, plus the test infrastructure to verify both the primary (`IExceptionHandler`) and fallback (middleware) exception-handling paths.

### 13.1 Endpoint extension with auth-guard warning

`src/PoliPage.AspNetCore/Endpoints/EndpointRouteBuilderExtensions.cs`:

```csharp
namespace PoliPage.AspNetCore;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapPoliPageSmokeTest(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/poli-page/smoke")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var convention = endpoints.MapGet(pattern, async (
            PoliPageClient client,
            CancellationToken cancellationToken) =>
        {
            var pdf = await client.Render.PdfAsync(
                new ProjectModeInput
                {
                    Project = "getting-started",
                    Template = "welcome",
                    Version = "1.0.0",
                    Data = new { name = "PoliPage.AspNetCore" },
                },
                cancellationToken: cancellationToken);

            return PoliPageResults.Pdf(pdf, "welcome.pdf", inline: true);
        });

        convention.Add(eb => eb.Metadata.Add(new PoliPageSmokeEndpointMarker()));

        endpoints.ServiceProvider
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted
            .Register(() => WarnIfSmokeEndpointIsUnguarded(endpoints));

        return convention;
    }

    private static void WarnIfSmokeEndpointIsUnguarded(IEndpointRouteBuilder endpoints)
    {
        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("PoliPage.AspNetCore.SmokeTest");

        foreach (var endpoint in endpoints.DataSources.SelectMany(s => s.Endpoints))
        {
            if (endpoint.Metadata.GetMetadata<PoliPageSmokeEndpointMarker>() is null) continue;
            if (endpoint.Metadata.GetMetadata<AuthorizeAttribute>() is not null) return;
            if (endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>() is not null) return;
            Internal.LogMessages.SmokeEndpointUnguarded(logger);
            return;
        }
    }
}

internal sealed class PoliPageSmokeEndpointMarker { }
```

`LogMessages.SmokeEndpointUnguarded` is already defined in Task 12's `Internal/LogMessages.cs`; this task adds the registration site. Task 12 must therefore land before Task 13 (the plan's task numbering already reflects this).

### 13.2 `StubPoliPageHttpHandler` — replaces the `FakePoliPageClient` subclass

`PoliPageClient`, `Render`, and `Documents` are all `sealed` with `internal` constructors (see `docs/sdk-surface-audit-2026-06-01.md` §0.2), so subclassing the client is impossible. Instead, drive the SDK through a `DelegatingHandler` that returns canned responses, injected via `PoliPageClientOptions.HttpClient` and `PoliPageClientOptions.DownloadHttpClient` (both are public extension points on the SDK options record).

`tests/PoliPage.AspNetCore.Tests/Fixtures/StubPoliPageHttpHandler.cs`:

```csharp
namespace PoliPage.AspNetCore.Tests.Fixtures;

// One DelegatingHandler used for *both* the API HttpClient and the download HttpClient.
// Inspect the request URI to decide which canned response to return:
//   - POST /v1/render        → descriptor JSON pointing at a stub presigned URL
//   - GET  /v1/render/preview→ HTML preview JSON
//   - GET  /storage/…        → the configured PDF bytes
// When .NextException is set, every call throws it so the IExceptionHandler / middleware
// can be exercised end-to-end without depending on real API behaviour.
internal sealed class StubPoliPageHttpHandler : DelegatingHandler
{
    public byte[] PdfBytes { get; set; } = "%PDF-1.7\n%stub"u8.ToArray();
    public string PreviewHtml { get; set; } = "<h1>preview</h1>";
    public PoliPageException? NextException { get; private set; }

    public void SetException(PoliPageException ex) => NextException = ex;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (NextException is { } ex) throw ex;

        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/v1/render", StringComparison.Ordinal))
        {
            var descriptor = $$"""
                {
                  "documentId": "doc_stub",
                  "organizationId": "org_stub",
                  "projectSlug": "getting-started",
                  "templateSlug": "welcome",
                  "version": "1.0.0",
                  "environment": "test",
                  "format": "pdf",
                  "pageCount": 1,
                  "sizeBytes": {{PdfBytes.Length}},
                  "presignedPdfUrl": "https://stub.invalid/storage/doc_stub.pdf"
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(descriptor, Encoding.UTF8, "application/json"),
            };
        }

        if (path.Contains("/storage/", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PdfBytes)
                {
                    Headers = { ContentType = new("application/pdf") },
                },
            };
        }

        if (path.EndsWith("/v1/render/preview", StringComparison.Ordinal))
        {
            var preview = $$"""{"pages":[{{"\""}}{{PreviewHtml.Replace("\"", "\\\"")}}{{"\""}}],"pageCount":1}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(preview, Encoding.UTF8, "application/json"),
            };
        }

        await Task.CompletedTask;
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
```

**Discipline reminder (CLAUDE.md §4)**: this stub returns *one* canned happy-path response per request shape — it is **not** a WireMock-style SDK retest. We are not exercising retry budgets, timeout behaviour, or 4xx→exception mapping here. If the test you're about to write needs more than this stub provides, you're testing the SDK, not the integration. Stop.

### 13.3 `PoliPageWebApplicationFactory` — primary path

`tests/PoliPage.AspNetCore.Tests/Fixtures/PoliPageWebApplicationFactory.cs`:

```csharp
public sealed class PoliPageWebApplicationFactory : WebApplicationFactory<TestProgram>
{
    public StubPoliPageHttpHandler Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Rebuild the PoliPage options registration so the SDK's HttpClient and
            // DownloadHttpClient are both backed by the stub. This is the only seam the
            // SDK provides for injecting canned responses without hitting the network.
            services.PostConfigure<PoliPageClientOptions>(opts =>
            {
                opts.HttpClient = new HttpClient(Stub) { BaseAddress = new("https://api.stub.invalid") };
                opts.DownloadHttpClient = new HttpClient(Stub);
            });
        });
    }
}

public partial class TestProgram { }
```

`tests/PoliPage.AspNetCore.Tests/Fixtures/TestProgram.cs` — primary path host:

```csharp
using PoliPage.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_factory");
//                ^ defaults: RegisterExceptionHandler = true, AddProblemDetailsService = true
var app = builder.Build();

app.UseExceptionHandler();                          // primary path — runs the auto-registered IExceptionHandler
app.MapPoliPageSmokeTest().AllowAnonymous();        // silence the auth-guard warning in tests

await app.RunAsync();

public partial class TestProgram { }
```

### 13.4 `FallbackPathWebApplicationFactory` — middleware path

`tests/PoliPage.AspNetCore.Tests/Fixtures/FallbackPathWebApplicationFactory.cs`:

```csharp
public sealed class FallbackPathWebApplicationFactory : WebApplicationFactory<TestProgram_Fallback>
{
    public StubPoliPageHttpHandler Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<PoliPageClientOptions>(opts =>
            {
                opts.HttpClient = new HttpClient(Stub) { BaseAddress = new("https://api.stub.invalid") };
                opts.DownloadHttpClient = new HttpClient(Stub);
            });
        });
    }
}

public partial class TestProgram_Fallback { }
```

`tests/PoliPage.AspNetCore.Tests/Fixtures/TestProgram_Fallback.cs` — middleware-path host:

```csharp
using PoliPage.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPoliPageAspNetCore(
    opts => opts.ApiKey = "pp_test_factory",
    aspnet =>
    {
        aspnet.RegisterExceptionHandler = false;    // disable IExceptionHandler registration
        aspnet.AddProblemDetailsService = true;     // keep IProblemDetailsService so the middleware can delegate
    });

var app = builder.Build();

app.UsePoliPageExceptionHandler();                  // fallback path — the middleware
app.MapPoliPageSmokeTest().AllowAnonymous();

await app.RunAsync();

public partial class TestProgram_Fallback { }
```

Two distinct `TestProgram*` partial classes are necessary because `WebApplicationFactory<T>`'s `T` is the entry-point marker: one boots primary-path code, the other boots fallback-path code. The fallback factory is referenced by Task 12's fallback-path test suite (`PoliPageExceptionHandlerMiddlewareTests`).

### 13.5 Endpoint tests

```csharp
public class MapPoliPageSmokeTestTests : IClassFixture<PoliPageWebApplicationFactory>
{
    private readonly PoliPageWebApplicationFactory _factory;

    public MapPoliPageSmokeTestTests(PoliPageWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Smoke_endpoint_returns_pdf_inline()
    {
        _factory.Stub.PdfBytes = "%PDF-1.7\n%fake"u8.ToArray();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(_factory.Stub.PdfBytes);
    }

    [Fact]
    public async Task Smoke_endpoint_throws_to_handler_on_sdk_failure()
    {
        _factory.Stub.SetException(
            new PoliPageAuthException(PoliPageErrorCode.Unauthorized, 401, "Bad key", requestId: "req_x"));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

### 13.6 Auth-guard warning tests

`tests/PoliPage.AspNetCore.Tests/Endpoints/SmokeEndpointAuthGuardTests.cs`:

```csharp
public class SmokeEndpointAuthGuardTests
{
    [Fact]
    public async Task Warns_when_smoke_endpoint_registered_without_auth_metadata()
    {
        var log = new InMemoryLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        builder.Services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        var app = builder.Build();
        app.MapPoliPageSmokeTest();   // no .RequireAuthorization / .AllowAnonymous

        await app.StartAsync();
        await app.StopAsync();

        log.Entries.Should().Contain(e =>
            e.EventId.Id == 2001 && e.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task Does_not_warn_when_RequireAuthorization_is_chained()
    {
        var log = new InMemoryLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        builder.Services.AddAuthorization(o => o.AddPolicy("Operator", p => p.RequireAuthenticatedUser()));
        builder.Services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        var app = builder.Build();
        app.MapPoliPageSmokeTest().RequireAuthorization("Operator");

        await app.StartAsync();
        await app.StopAsync();

        log.Entries.Should().NotContain(e => e.EventId.Id == 2001);
    }

    [Fact]
    public async Task Does_not_warn_when_AllowAnonymous_is_chained()
    {
        var log = new InMemoryLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        builder.Services.AddPoliPageAspNetCore(opts => opts.ApiKey = "pp_test_x");

        var app = builder.Build();
        app.MapPoliPageSmokeTest().AllowAnonymous();

        await app.StartAsync();
        await app.StopAsync();

        log.Entries.Should().NotContain(e => e.EventId.Id == 2001);
    }
}
```

`InMemoryLoggerProvider` is a trivial `ILoggerProvider` capturing `(EventId, LogLevel, message)` into a thread-safe list. Three-screen helper in `tests/.../Fixtures/InMemoryLoggerProvider.cs`.

---

## Task 14: ~~`IHealthChecksBuilder.AddPoliPage` + tests~~ — **deferred to v0.2**

**Status (2026-06-01)**: deferred. The SDK does not currently expose a `PingAsync` (or any equivalent cheap GET), so a first-class health-check integration would have to either (a) render `getting-started/welcome` as a probe — wasteful, burns API quota on every poll — or (b) block on an `sdk-csharp` PR. See `docs/sdk-surface-audit-2026-06-01.md` §0.3.

**For hosts that need a Poli Page health check before v0.2**: register a one-line `IHttpClientFactory`-based probe against the published `MapPoliPageSmokeTest` URL. The README will ship the snippet. No code or package reference is added to this repo for v0.1.

**Re-enable when:**

1. `sdk-csharp` publishes a `public Task PingAsync(CancellationToken)` (or names a documented "cheapest GET"), and
2. v0.2 milestone opens.

At that point, re-add the `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` reference to `Directory.Packages.props` (Task 1.3) and `src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj` (Task 2.1), and re-introduce the `HealthChecks/` folder with `PoliPageHealthCheck` + `HealthChecksBuilderExtensions.AddPoliPage(...)`.

---

## Task 15: `example-app/` skeleton

**Goal**: a runnable ASP.NET Core 10 app exercising every endpoint.

**Layout**: see spec §13.3.

### 15.1 Scaffold

```bash
cd /Users/mickael/Projects/asp
dotnet new web -n example-app -f net10.0 -o example-app
cd example-app
dotnet add reference ../src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj
```

### 15.2 `Program.cs`

Per spec §13.4 — Minimal API + UsePoliPageExceptionHandler + MapPoliPageSmokeTest + endpoint group registrations.

### 15.3 Endpoint files

- `Endpoints/RenderEndpoints.cs` — extension on `IEndpointRouteBuilder` exposing `MapRenderEndpoints(this IEndpointRouteBuilder builder)` that maps demo steps 1, 2, 4.
- `Endpoints/DocumentEndpoints.cs` — demo steps 5-9.
- `Endpoints/ErrorEndpoints.cs` — demo step 10 (`/errors/bad-version` throws `PoliPageBadRequestException`).

### 15.4 `Controllers/InvoicesController.cs`

Demonstrates the MVC code path with `PoliPageResponseFactory`. Mapped via `builder.Services.AddControllers()` + `app.MapControllers()`.

### 15.5 `Scripts/RenderToFile.cs`

Demo step 3 — invoked when `args[0] == "render-to-file"`. Uses the SDK's `RenderToFileAsync` helper directly; no DI host built for the script path.

### 15.6 `wwwroot/demo.html`

Static file copied from `/Users/mickael/Projects/symfony-bundle/example-app/templates/demo.html`. Adjust the URLs to match the example app's endpoint paths.

Wire via:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/demo.html"));
```

### 15.7 `appsettings.json`

```json
{
  "PoliPage": {
    "ApiKey": "pp_test_replace_me",
    "AspNetCore": {
      "ProblemDetailsTypeUri": "https://poli.page/errors"
    }
  }
}
```

### 15.8 Workspace `.env` loader

Per spec §13.5 — `Scripts/PoliPageWorkspaceEnvFile.cs`. Wired via `builder.Configuration.AddPoliPageWorkspaceEnvFile()` near the top of `Program.cs`.

### 15.9 Verification

```bash
cd example-app
export $(grep POLI_PAGE_API_KEY /Users/mickael/Projects/.env)
dotnet run
```

Open `http://localhost:5093/` in a browser. Click each button on the dashboard — verify inline previews work, JSON pretty-prints, document lifecycle gates correctly, error endpoint surfaces ProblemDetails.

---

## Task 16: Integration tests against the develop API

**Goal**: one happy-path test confirming the wire works end-to-end.

### 16.1 Project

`tests/PoliPage.AspNetCore.IntegrationTests/PoliPage.AspNetCore.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/PoliPage.AspNetCore/PoliPage.AspNetCore.csproj" />
    <ProjectReference Include="../../example-app/example-app.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>
</Project>
```

### 16.2 Test

```csharp
public class RenderAgainstDevelopApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public RenderAgainstDevelopApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Smoke_endpoint_round_trips_against_develop()
    {
        if (Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY") is null)
            return;     // silent skip — CI clean when secret absent

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/poli-page/smoke");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1024);
    }
}
```

CI runs this only on `main` after merge — see Task 1's workflow.

---

## Task 17: README, CHANGELOG, MIGRATION fill — final docs pass

**Goal**: the published `README.md`, `CHANGELOG.md`, and `MIGRATION.md` accurately reflect v0.1.0.

- README: already drafted; review for accuracy against the actual public surface after Tasks 5–14. Pay attention to: option names matching the code, method signatures matching the actual extensions, exception class names matching the SDK.
- CHANGELOG: move the "Unreleased" Added section into the `[0.1.0] — <release-date>` block. Update the date to the merge date.
- MIGRATION.md: create with one section, "From 0.0.x to 0.1.0", documenting that 0.0.x never shipped publicly (pre-NuGet bootstrap). Empty body otherwise; the file exists so future migrations land in the same place.

---

## Task 18: Final verification + tag v0.1.0

**Goal**: ship.

### 18.1 Local pre-flight

```bash
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test --filter "Category!=Integration"
dotnet test --filter "Category=Integration"   # local-only, requires POLI_PAGE_API_KEY
dotnet pack -c Release
```

All green.

### 18.2 SDK publication check

Before tagging, verify `PoliPage` has published `1.0.0` to nuget.org:

```bash
curl -sS https://api.nuget.org/v3-flatcontainer/polipage/index.json | jq .versions
```

If `1.0.0` is not present, **stop** — tagging v0.1.0 of this package while the SDK is still local-only would publish a broken NuGet artifact. Coordinate with the `sdk-csharp` maintainer.

Once the SDK has published, remove the local source:

```bash
git rm nuget.config
```

Update `Directory.Packages.props`:

```xml
<PackageVersion Include="PoliPage" Version="1.0.0" />
```

```bash
dotnet restore   # confirm restoration from nuget.org
dotnet test --filter "Category!=Integration"   # confirm tests still pass against the published SDK
```

### 18.3 Tag

```bash
git commit -am "chore: lift PoliPage to 1.0.0 from nuget.org"
git tag v0.1.0
git push origin main --tags
```

### 18.4 NuGet push

A `release.yml` workflow (or manual `dotnet nuget push`) takes over from here. Out of scope for this plan — the release pipeline is a separate concern documented in `RELEASE.md` (not yet written; defer to v0.2 cadence).

---

## Self-review checklist (for the agent executing this plan)

Before opening each task's PR:

- [ ] **TDD**: red → green → refactor cycle visible in commit history.
- [ ] **One concern**: PR is reviewable in under 30 minutes; doesn't touch unrelated files.
- [ ] **Tests for the integration concern only**: no SDK transport mocking, no retry-policy tests, no idempotency tests.
- [ ] **No analyzer warnings**: `dotnet format --verify-no-changes` clean.
- [ ] **No `TODO` without an issue**: `TODO(#42): …` is fine, bare `TODO` is not.
- [ ] **XML docs on every new public symbol**: IntelliSense and the API reference depend on them.
- [ ] **`[InternalsVisibleTo]` only for the test assembly**: no friend access from anywhere else.
- [ ] **No `Console.WriteLine` debug prints** in committed code.
- [ ] **Conventional commit message**: `feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`.
- [ ] **README / CHANGELOG updated** when the PR adds or changes a public symbol.
- [ ] **CI green** on every cell of the matrix.

Before tagging v0.1.0 (Task 18):

- [ ] All 18 prior tasks merged (Task 0 audit report + Tasks 1–17 PRs).
- [ ] `dotnet test --filter "Category=Integration"` passes locally against `POLI_PAGE_API_KEY=pp_test_…`.
- [ ] Example app runs from `dotnet run` and every dashboard button works.
- [ ] `MapPoliPageSmokeTest` hit from `curl` returns a valid PDF (`%PDF-` magic + non-trivial length).
- [ ] `dotnet pack -c Release` produces a `.nupkg` containing both `net8.0` and `net10.0` assemblies and references `PoliPage 1.0.0` from nuget.org (not the local source).
- [ ] The published NuGet listing on nuget.org's preview renders the README correctly.
- [ ] The auto-generated API reference at `poli-page.github.io/asp` has no `<missing>` sections.

---

## Cross-references at a glance

| What | Where |
|---|---|
| Design decisions | `docs/spec/aspnet-core-specification.md` (esp. §4, §6, §7, §8, §10, §18) |
| Agent guardrails | `CLAUDE.md` (esp. §10 *Known gotchas*) |
| Cross-repo conventions | `/Users/mickael/Projects/INTEGRATIONS_PLAN.md` (esp. *Cross-cutting DX patterns*) |
| SDK surface | `/Users/mickael/Projects/sdk-csharp/src/PoliPage/` |
| SDK DI extension this package wraps | `/Users/mickael/Projects/sdk-csharp/src/PoliPage/DependencyInjection/ServiceCollectionExtensions.cs` |
| Sister bundle (PHP/Symfony) | `/Users/mickael/Projects/symfony-bundle/` |
| Sister bundle (PHP/Laravel) | `/Users/mickael/Projects/laravel/` |
| Sister bundle (TS/Nest) | `/Users/mickael/Projects/nestjs/` |
| RFC 5987 filename algorithm reference | `/Users/mickael/Projects/nextjs/src/responses/headers.ts` |
| Demo HTML to port | `/Users/mickael/Projects/symfony-bundle/example-app/templates/demo.html` |
| Workspace `.env` parser reference | `/Users/mickael/Projects/symfony-bundle/tests/bootstrap.php` (PHP), `/Users/mickael/Projects/nestjs/example-app/src/main.ts` (TS) |
| Industry benchmarks | `Sentry.AspNetCore`, `Serilog.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Hangfire.AspNetCore`, `OpenTelemetry.Extensions.Hosting` |

When stuck on a slice: re-read the matching spec section first; then check the sister bundle for the equivalent decision; then ask Mickael.
