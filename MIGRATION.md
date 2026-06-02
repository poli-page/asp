# Migration guide

This file documents breaking changes between releases of `PoliPage.AspNetCore`. Future migrations land here so consumers always know where to look.

## From 0.0.x to 0.1.0

`0.0.x` never shipped publicly — `0.1.0` is the first release on NuGet. No migration is required.

If you were tracking the package via the local `nuget.config` source while it was unpublished, switch back to the public registry:

1. Remove the `poli-page-local` `<add key="…"/>` line from your `nuget.config` (or delete `nuget.config` entirely if it had no other entries).
2. Run `dotnet restore` — the resolver picks `PoliPage.AspNetCore 0.1.0` from `nuget.org` automatically.
