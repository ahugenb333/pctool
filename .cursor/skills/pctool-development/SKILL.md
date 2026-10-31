---
name: pctool-development
description: Architecture, data-file conventions and verification steps for the PcTool Windows app (.NET 8, WPF-UI). Use when working in this repository, adding or updating games, CPU/GPU tiers, chipsets, parts or prices under data/, changing the compatibility, game, upgrade or diagnostics engines, or adding UI pages.
---

# PcTool development

## Architecture

- Engines are UI-free libraries; `PcTool.App` (WPF) and `tools/PcTool.Cli` both consume them.
- Detected hardware (`HardwareProfile`) is resolved by `SystemSpecsBuilder` into `SystemSpecs` (tiers + user overrides). All engines take `SystemSpecs`, never the raw profile.
- Unknown facts stay `null` (chipset, PSU, case). Rules must return `CheckStatus.Unknown` ("couldn't verify"), not `Fail`.
- Monetizable data stays behind `IGameCatalog`, `IPartsCatalog`, `IPriceSource`. Don't reference the JSON files from engines.
- `net8.0` for portable logic (Core, Data, Games, Compatibility, Upgrades); `net8.0-windows` only for Hardware, Benchmarks, Diagnostics, App.
- DI registration lives in `src/PcTool.App/App.xaml.cs`. Pages are `Page` + view model pairs registered as singletons; `AppState.SpecsChanged` drives recalculation.

## Data files (embedded at build time)

Everything under `data/` is embedded into `PcTool.Data`; rebuild after edits.

**Tier ids** are `NameMatcher.Slug(name)`: lowercase, non-alphanumerics to `-`. "RTX 4070 Super" -> `rtx-4070-super`, "Core i5-13600K" -> `core-i5-13600k`.

**Scores**: CPU single/multi indexed to Ryzen 5 5600X = 100; GPU raster indexed to RTX 3060 12GB = 100. Place new parts relative to neighbours from aggregate review consensus. Never copy PassMark or other proprietary benchmark databases.

### Add a GPU — `data/tiers/gpus.csv`

`name,vendor,score,vram,tdp,rt,upscaling,dx,year,kind,aliases`

- `kind`: `d` desktop, `l` laptop, `i` integrated. `rt`: 0/1. `dx`: feature level like `12_2`.
- `aliases`: `;`-separated, lowercase, matched as whole words; the longest matching alias wins. Add an alias whenever a shorter name is a prefix of another ("rtx 4070" vs "rtx 4070 super") only if detection would otherwise pick the wrong one — check with the tests below.

### Add a CPU — `data/tiers/cpus.csv`

`name,vendor,family,socket,mem,cores,threads,tdp,single,multi,year,kind,gaming,aliases`

- `family` must match names used in `data/platforms/chipsets.csv` `biosUpdate`/`unsupported` (e.g. `Ryzen 5000`, `Core 14th Gen`).
- `gaming` is 1, or ~1.25–1.35 for X3D parts. `socket` is `BGA` for laptop chips (marks the system as a laptop).

### Add a game — `data/games/games.json`

```json
{ "id": "slug-of-name", "name": "Game Name", "steamAppId": 123, "year": 2025, "genres": ["Action"],
  "ssdRequired": true, "rayTracingRequired": false, "executables": ["Game-Win64-Shipping.exe"],
  "minimum": { "cpu": "core-i5-8400", "gpu": "gtx-1060-6gb", "ramGb": 8, "vramGb": 6, "storageGb": 70, "directX": 12 },
  "recommended": { "cpu": "ryzen-5-5600x", "gpu": "rtx-3060", "ramGb": 16, "vramGb": 8, "storageGb": 70, "directX": 12 } }
```

Use the publisher's listed specs. When both Intel and AMD parts are listed, pick the closer tier. `executables` drive crash matching in Diagnostics.

### Add parts and prices

- Parts use the TechFuelHQ snake_case schema. Put new rows in `data/parts/pctool-supplemental-parts.json` (never edit `techfuelhq-parts.json`; it is a CC BY 4.0 upstream snapshot).
- Every CPU/GPU part model must resolve to a tier, every chipset must exist in `chipsets.csv`, and every part id needs an entry in `data/prices/reference-prices.json`.

## Verify

Run after any engine or data change:

```powershell
dotnet build PcTool.sln
dotnet test tests/PcTool.Logic.Tests
```

`DataIntegrityTests` catch unresolved tier ids, unpriced parts, unknown chipsets and min > recommended specs. For a new detected-name pattern, add an `[InlineData]` case to `TierMatchingTests`.

Spot-check behaviour on the local machine with the CLI:

```powershell
dotnet run --project tools/PcTool.Cli -- games
dotnet run --project tools/PcTool.Cli -- upgrades --goal Gaming1440p --budget 600
dotnet run --project tools/PcTool.Cli -- check --part "<model text>" --psu 650
```

## Gotchas

- WPF projects don't get `System.IO` as an implicit using; add it explicitly.
- `EmbeddedCatalogs.Parts` is the concrete `PartsCatalog`; call `.Parts.OfType<T>()`, not the interface default `OfType<T>()`.
- DXGI reports VRAM in GiB slightly under the marketed size; `GpuInfo.VramGb` already rounds — don't round again.
- Benchmark reference constants (`CpuBenchmark.SingleReference/MultiReference`, `GpuBenchmark.ReferenceFps`) are calibrated so measured scores line up with the tier scale. If you change a workload, recalibrate against a machine whose tier scores are known, using a Release build (`dotnet run -c Release --project tools/PcTool.Cli -- bench`); Debug builds are several times slower. For GPU: `ReferenceFps = measuredFps * 100 / gpuTierScore` on a known card (e.g. RTX 3070 = 140). The 3D test is compute-heavy; the Benchmarks page shows the tier raster index when the GPU is in the database.
- New third-party packages: check the license (avoid non-commercial data like BuildCores OpenDB) and add them to `THIRD-PARTY-NOTICES.txt` and the Settings page list.
