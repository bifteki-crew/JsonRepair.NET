# JsonRepair.NET: Implementation Roadmap (.NET 10)

## Overview

This roadmap defines the step-by-step phased execution plan for constructing **`JsonRepair.NET`**, starting from initial solution scaffolding to core state-machine implementation, testing, benchmarking, CLI tool creation, and packaging.

---

## Phase Breakdown

### Phase 1: Solution & Project Infrastructure (Current Phase)
- **Deliverables**:
  - Scaffold .NET 10 Solution (`JsonRepair.sln`).
  - Core Library (`JsonRepair.csproj` -> `net10.0`).
  - Unit Test Project (`JsonRepair.Tests.csproj` -> xUnit, FluentAssertions).
  - CLI Tool (`JsonRepair.Cli.csproj` -> Spectre.Console).
  - Benchmark Suite (`JsonRepair.Benchmarks.csproj` -> BenchmarkDotNet).
  - Configure global options (`JsonRepairOptions.cs`).

---

### Phase 2: Core Tokenization & State Machine Engine
- **Deliverables**:
  - Implement `JsonCharBuffer` for zero-allocation windowed scanning.
  - Implement `ref struct JsonRepairStateMachine` parser loop.
  - Rule 1: Code fence stripping (` ```json `, ` ``` `).
  - Rule 2: Quote normalizer (single quotes to double quotes, escaping).
  - Rule 3: Unquoted key detector and auto-quoting.
  - Rule 4: Python/JS literal converter (`None` -> `null`, `True` -> `true`, `False` -> `false`, `undefined` -> `null`).
  - Rule 5: Trailing comma & missing comma corrector.
  - Rule 6: Truncated JSON balancer (closing unclosed `{`, `[`, `"`).

---

### Phase 3: System.Text.Json Integration & Extension Methods
- **Deliverables**:
  - Add extension methods: `string.RepairJson()`.
  - Add `JsonRepairEngine.TryParse(string, out JsonDocument)`.
  - Add `System.Text.Json` converter hooks (`JsonRepairConverter<T>`).

---

### Phase 4: CLI Application & Interactive Tool
- **Deliverables**:
  - Build `JsonRepair.Cli` using `Spectre.Console`.
  - Interactive terminal window supporting copy-pasting malformed LLM JSON prompts and outputting instant repaired JSON with colored diff formatting.

---

### Phase 5: Micro-benchmarking & Performance Optimization
- **Deliverables**:
  - Benchmark `JsonRepair.NET` against naive Regex replacements and standard `System.Text.Json`.
  - Measure execution throughput (ops/sec) and allocations (Bytes/op).
  - Verify zero-heap-allocation target for span-based repair buffers under 4KB.

---

### Phase 6: Packaging & Documentation Release
- **Deliverables**:
  - Generate NuGet package specification (`JsonRepair.csproj` metadata).
  - Write `README.md` with usage examples for LLM pipelines (Semantic Kernel, AutoGen, OpenAI API).
  - Add GitHub Actions CI workflow for test execution and benchmarking.
