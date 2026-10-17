# JsonRepair.NET: Implementation Roadmap (.NET 10)

## Overview

This document is the **historical** build-out plan. It is not the live schedule. Phases below are marked with what actually shipped. Ongoing work — 0.3.0 and later — is [05-pre-1.0-roadmap.md](05-pre-1.0-roadmap.md).

The solution file is [`JsonRepair.slnx`](../JsonRepair.slnx) (SDK-style XML). There is no `JsonRepair.sln`.

---

## Phase Breakdown

### Phase 1: Solution & Project Infrastructure — done

- Scaffolded the .NET 10 solution as `JsonRepair.slnx`.
- Core library `src/JsonRepair/JsonRepair.csproj` (`net10.0`).
- Unit tests `tests/JsonRepair.Tests` — xUnit 2.9.3 and AwesomeAssertions 9.6.0 (FluentAssertions was replaced; see `CHANGELOG.md` `[Unreleased]`).
- Demo CLI `src/JsonRepair.Cli` — Spectre.Console, `IsPackable=false`.
- Benchmarks `benchmarks/JsonRepair.Benchmarks` — BenchmarkDotNet, `IsPackable=false`.
- `JsonRepairOptions` is in `src/JsonRepair/Options/JsonRepairOptions.cs`.

### Phase 2: Core Tokenization & State Machine Engine — done, without `JsonCharBuffer`

`JsonCharBuffer` was not built. Scanning lives inside two `ref struct` engines:

- `JsonRepairStateMachine` walks a `ReadOnlySpan<char>` and appends to a `StringBuilder`.
- `Utf8JsonRepairStateMachine` walks UTF-8 bytes and writes an `IBufferWriter<byte>`.

Repair rules that shipped:

1. Markdown fence stripping.
2. Single quotes normalised to double quotes.
3. Unquoted keys quoted.
4. Python/JS literals (`None` → `null`, `True` → `true`, `False` → `false`, `undefined` → `null`, `NaN` → `null`), including the 0.1.0 fix that does not convert those words when they are keys.
5. Trailing commas removed and missing commas inserted.
6. Truncated JSON closed (`{`, `[`, and an unclosed `"`).

### Phase 3: System.Text.Json Integration & Extension Methods — reduced

| Deliverable | What happened |
| :--- | :--- |
| `string.RepairJson()` and other extension methods on `JsonDocument` / `JsonNode` / `JsonSerializer` | **Cut.** Not in the public API. |
| `JsonRepairEngine.TryParse(string, out JsonDocument?)` | **Shipped.** |
| `Deserialize<T>` / `TryDeserialize<T>` | **Shipped** on `JsonRepairEngine` (not as extensions). |
| `JsonRepairConverter<T>` / `[JsonRepair]` | **Cut in 0.1.0** after a stack-overflow in the converter. Redesign is deferred to **0.4.0**. See T5 in [05-pre-1.0-roadmap.md](05-pre-1.0-roadmap.md). |

### Phase 4: CLI Application & Interactive Tool — reduced

`JsonRepair.Cli` exists as a **non-packable demo**. It prints a hardcoded sample through Spectre.Console. It does not read pasted input, it is not a `dotnet tool`, and `IsPackable` is `false`. A real stdin/stdout CLI is deferred to 0.5.0+ in the pre-1.0 roadmap.

### Phase 5: Micro-benchmarking & Performance Optimization — reduced

- BenchmarkDotNet covers `Repair(string)` and `Repair(ReadOnlySpan<byte>, IBufferWriter<byte>)` at three payload sizes, plus `TryParse` on the small payload. Figures are in the README.
- A comparison against naive regex replacements was **not built**. Any 10×–50× claim against regex or Python is unmeasured.
- The old "zero heap allocations for span buffers under 4 KB" target does not describe the code. `Repair(string)` allocates the result `string`. The UTF-8 path measures **0 B/op at every size in the README** when the caller reuses the writer, because the validation staging buffer is pooled per thread — not because output is written into a stack `Span<char>`.

### Phase 6: Packaging & Documentation Release — done for 0.1.0 and 0.2.0

- NuGet package `JsonRepair` (0.1.0 on 2026-07-25, 0.2.0 on 2026-08-29).
- `README.md` with the repair contract, measured limits, and benchmark tables.
- GitHub Actions: `ci.yml` on push, `benchmarks.yml` on `workflow_dispatch`, `publish.yml` on version tags, `upstream-watch.yml` weekly.

---

## Where to look next

[05-pre-1.0-roadmap.md](05-pre-1.0-roadmap.md) is the live plan (0.3.0 through 1.0.0). This file stays as the record of what the original six phases became.
