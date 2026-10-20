# Changelog

All notable changes to `JsonRepair.NET` are documented here.

The project follows the 0.x hardening tiers in
[docs/05-pre-1.0-roadmap.md](docs/05-pre-1.0-roadmap.md): each minor may contain breaking API
changes, each patch is bug fixes only. Pin an exact minor in production.

---

## [Unreleased]

### Added

- **Unquoted string values** are repaired on both the string and UTF-8 engines when
  the value is inside an object or array. `{a: hello}` becomes `{"a":"hello"}`,
  `{greeting: hello world}` becomes `{"greeting":"hello world"}`, and
  `{a: TrueStuff}` becomes `{"a":"TrueStuff"}`. Keys are still quoted as identifiers.
  `$` is an identifier character in keys, so `{$ref: 1}` becomes `{"$ref":1}` and
  `{foo$: 1}` becomes `{"foo$":1}`. `true`, `false`, `null`, and the existing
  Python/JS literals (`None`, `True`, `False`, `undefined`, `NaN`) stay literals
  when a real token boundary follows them (`null@example.com` is a string, not
  `null`). Whitespace-split numbers such as `8 67` still throw. Root prose such as
  `I cannot help with that.` is not quoted and still throws. Measured josdejong
  corpus parity on the string engine is **215/427** (was 191/427).

### Security

- Removed the `Microsoft.SourceLink.GitHub` package reference, which pulled
  `Microsoft.Build.Tasks.Git` 8.0.0 — subject to CVE-2026-62900 (.NET information disclosure,
  medium), with no patched version on the 8.0.x line. **Published packages were never affected**:
  SourceLink was a build-time-only reference (`PrivateAssets="All"`) and `JsonRepair` ships with
  zero runtime dependencies. SourceLink itself is unchanged — it has been part of the .NET SDK
  since .NET 8, so the package was redundant; the commit-pinned source mapping is still in the
  symbol package.
- CI now fails on any vulnerable package, direct or transitive. Dependabot alerts are enabled on
  this repository but did not flag the above, so this closes that gap.
- Added `.github/dependabot.yml` for weekly NuGet and GitHub Actions version updates.

### Changed

- Unquoted values inside an object or array now keep `/` and `+`. `{file: src/app.cs}`
  becomes `{"file":"src/app.cs"}`, `{path: a/b}` becomes `{"path":"a/b"}`, and
  `{lang: C++}` becomes `{"lang":"C++"}`. `//` and `/*` still start a comment at a
  token boundary, including after a number, and a comment is itself a boundary so
  `n/*x*/ull` is not repaired to `null`. A carriage return ends an unquoted value
  the same way a line feed does. Non-ASCII digits such as `١` are text, not JSON
  numbers, on both engines. Corpus parity stays **215/427**.
- A comment between numbers is a separator, so `1/*x*/2` throws instead of becoming
  `12`, while `[1/*x*/2]` stays `[1, 2]`. `//` and `/*` end a literal (`[true/*x*/]`
  stays `[true]`); a single `/` does not (`[true/x]` is a string). An object key may
  mix ASCII digits with other decimal digits (`{1١:1}`). An apostrophe inside a URL
  stays in the URL (`https://example.com/O'Reilly`).
- Inputs that used to throw now repair when the unquoted text is a value inside an
  object or array. `{a: hello}` becomes `{"a":"hello"}` and `{a: TrueStuff}` becomes
  `{"a":"TrueStuff"}`. The same text at the root, such as `I cannot help with that.`,
  still throws. `$` in an object key is kept as part of the key (`{$ref: 1}`,
  `{foo$: 1}`).
- **Replaced FluentAssertions with [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions)
  9.6.0 (Apache-2.0).** FluentAssertions changed licence at 8.0 to the Xceed Community License,
  which permits non-commercial use only and requires a paid commercial licence otherwise. An
  MIT-licensed project should not need a commercial licence to run its own test suite, and
  contributors working in a commercial context would have needed one. AwesomeAssertions is the
  Apache-2.0 community fork; the assertion API is unchanged, so only the `using` lines moved.
  Test-only; the shipped package has no dependencies either way.
- Test tooling updated: `Microsoft.NET.Test.Sdk` 17.14.1 → 18.10.0, `coverlet.collector`
  6.0.4 → 10.0.1, `xunit.runner.visualstudio` 3.1.4 → 4.0.0. Test-only; not shipped.

---

## [0.2.0] — 2026-08-29 — Valid-or-throw contract

### ⚠️ Breaking changes

**`Repair` now throws instead of returning best-effort text.** Previously it returned whatever the
state machine produced, which for unrepairable input was invalid JSON — the failure surfaced later
at your `JsonSerializer.Deserialize` call with a confusing message. It now validates before
returning and throws `JsonRepairException` when the result would not parse.

```csharp
// Before 0.2.0 — could return invalid JSON that failed later
string repaired = JsonRepairEngine.Repair(input);

// 0.2.0 — either valid JSON, or a JsonRepairException naming the reason
try {
    string repaired = JsonRepairEngine.Repair(input);
}
catch (JsonRepairException ex) {
    // ex.Message explains why; ex.InnerException carries the raw parser offsets
}

// Or avoid the exception entirely
if (JsonRepairEngine.TryRepair(input, out string? repaired)) {
    // ...
}
```

`JsonRepairException` derives from `JsonException`, so existing `catch (JsonException)` clauses
keep working. `TryParse` and `TryDeserialize` are unaffected — they already returned `false`.

**`JsonRepairOptions.NormalizeQuotes` removed.** Setting it to `false` emitted single-quoted output
such as `{'a':'b'}`, which is not valid JSON and directly contradicts the new contract. Single
quotes are now always normalised. There is no replacement; remove the property from your options
initialiser.

### Added

- `JsonRepairException`, thrown when input cannot be repaired into valid JSON.
- `TryRepair(string, out string?, JsonRepairOptions?)` and
  `TryRepair(ReadOnlySpan<byte>, IBufferWriter<byte>, JsonRepairOptions?)`. The UTF-8 overload
  writes nothing to your buffer when repair fails.
- Ported josdejong test corpus (435 cases) running against both engines, with a categorised
  known-failure baseline that can only shrink. See [docs/UPSTREAM.md](docs/UPSTREAM.md).
- Property-based fuzz harness over generated and corrupted JSON, with fixed seeds for
  reproducibility. Set `JSONREPAIR_FUZZ_SEEDS` to widen a local run.

### Fixed

- **The UTF-8 engine fabricated numbers.** Adjacent root-level values were run together, so
  `8 67` repaired to `867` and `-2 7.241` to `-27.241` — valid JSON carrying a number that was
  never in the input, which the contract therefore could not catch. `n ull` became `null` the same
  way. Both engines now reject these, matching both upstreams, neither of which ever fuses tokens.
- **Lone surrogates escaped the contract.** A truncated emoji — what an LLM emits when a token
  boundary lands mid-pair — made `Repair` throw `ArgumentException`, and made `TryRepair` throw
  rather than return `false`.
- **Deep nesting past 64 levels was rejected.** The new validators used `MaxDepth = 0`, which
  System.Text.Json reads as the 64-level default rather than unlimited, so correctly repaired
  output nested deeper than 64 was refused.
- **The string engine could not find JSON after prose or comments.** `Here is your JSON:\n980`
  repaired on the UTF-8 engine and threw on the string engine.
- **Failure messages quoted the wrong position.** The reported offset described the repaired
  output while the message spoke about the input, sending you to the wrong place. The reason is
  kept, the misleading offset is not, and the raw values remain on `InnerException`.
- Both engines now report the same reason for the same input; previously the UTF-8 engine returned
  a bare "Unable to repair the input into valid JSON."

### Performance

Re-measured on an Apple M5 Pro (.NET 10.0.11, Arm64); see the README for the full table.

**The UTF-8 span path allocates 0 B per call, at every payload size**, preserving 0.1.0's
guarantee. The valid-or-throw contract needs a staging buffer so a partially-repaired document
never reaches your writer; that buffer is pooled and reused per thread, and its backing array is
returned to `ArrayPool<byte>.Shared` after each call, so a thread that repairs one large document
does not then pin a large array. A re-entrant repair — your `IBufferWriter` calling back into the
engine while it is being written to — is handed its own buffer rather than corrupting the outer
one.

The UTF-8 path is faster at every size measured on x64 — 1.9× small, 1.7× medium, 1.5× large.
On Arm64 the large-payload case inverts and measures ~20% slower than the `string` API; that is
reproducible on Apple Silicon, absent on x64, and not yet explained.

Benchmark coverage was incomplete before this release: the suite had no UTF-8 benchmark at all, so
the zero-allocation claim had never actually been measured. It has three now, and `benchmarks.yml`
invokes BenchmarkDotNet properly — it was missing `--run`, so it only ever executed a stopwatch
loop.

### Known limitations

Input-relative error positions are **not** in this release, despite the tier description. The
engine repairs optimistically and validates afterwards, so an offset into the repaired output is
the only one it has; input positions need the grammar-based rework scheduled for 0.3.0. See
[docs/05-pre-1.0-roadmap.md](docs/05-pre-1.0-roadmap.md).

Upstream parity is **191/427 (44.7%)** of the ported josdejong corpus, with all 242 gaps
categorised in [docs/UPSTREAM.md](docs/UPSTREAM.md). The largest are unquoted string values (69)
and number normalisation (32), both scheduled for 0.3.0.

---

## [0.1.0] — 2026-07-25 — Initial release

First public release on [nuget.org](https://www.nuget.org/packages/JsonRepair/).

Single-pass `ref struct` state machines repairing markdown fences, single quotes, unquoted keys,
Python/JS literals (`None`, `True`, `False`, `undefined`, `NaN`), trailing commas, comments,
mismatched brackets and truncated/unclosed input. String API plus a UTF-8
`ReadOnlySpan<byte>` / `ReadOnlySequence<byte>` / `Stream` API writing into an `IBufferWriter<byte>`.

Resolved before release: converter recursion crash (the `[JsonRepair]` attribute and
`JsonRepairConverterFactory` were cut, to be redesigned in 0.4.0), literal conversion firing in key
position and without a word boundary, mismatched brackets never being repaired, and
`TryDeserialize<T>` returning `false` for a legitimately null value.
