# Upstream Source Tracking & Synchronization Log

`JsonRepair.NET` is a clean-room, native .NET 10 implementation inspired by the algorithms and test cases of the following open-source projects:

## Upstream References

| Upstream Project | Primary Author | Language | License | Tracked Repository | Pinned Version / Commit |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`jsonrepair`** | Jos de Jong | JavaScript | ISC | [josdejong/jsonrepair](https://github.com/josdejong/jsonrepair) | `v3.15.0` |
| **`json_repair`** | Mangiucugna | Python | MIT | [mangiucugna/json_repair](https://github.com/mangiucugna/json_repair) | `v0.61.7` |

---

## Measured Subset (as of 0.2.0)

The josdejong test suite is ported into
`tests/JsonRepair.Tests/TestCases/UpstreamCorpus/josdejong-corpus.json` and runs against **both**
engines on every build. Comparison is semantic — object order-insensitive, numbers by value
(`1 == 1.0 == 1e0`) — so formatting differences do not count as failures.

| Metric | Value |
| :--- | :--- |
| Corpus cases ported | **435** (427 with an expected output, 8 where upstream throws) |
| Passing on the string engine | **191 / 427 (44.7%)** |
| Tracked as known gaps | **242** entries in `josdejong-baseline.json` |
| Of those, currently failing | 236 (216 rejected by the repair contract, 20 repaired to different JSON) |

`josdejong-baseline.json` may only shrink: `Baseline_ShouldOnlyContainCurrentlyFailingCases` fails
the build if a baselined case starts passing on both engines, and `ParityReport` fails if any
untracked case regresses. Neither number moves silently.

### Gap categories

Every baselined case carries a category, so the subset is enumerable rather than adjectival.
Counts below sum to the 242 baseline entries.

| Category | Cases | Scheduled |
| :--- | ---: | :--- |
| `unquoted-values` — `{a: hello}` | 69 | Tier 3 (0.3.0) |
| `number-normalization` — `2.`, `-.5`, `001`, `2e` | 32 | Tier 3 |
| `missing-quote-heuristics` — `{'it's'}` | 18 | Tier 3 |
| `ellipsis` — `[1, 2, ...]` | 18 | Tier 3 |
| `html-entities` — `&quot;` | 16 | Tier 5 (0.5.0+) |
| `smart-quotes` — `“…”` `‘…’` | 14 | Tier 3 |
| `missing-colon` — `{a 1}` | 10 | Tier 3 |
| `special-whitespace` — NBSP, U+3000 | 9 | Tier 3 |
| `leading-commas` — `[,1]` | 8 | Tier 3 |
| `ndjson` — newline-delimited roots | 7 | Tier 4 (0.4.0) |
| `missing-value-null` — `{"a":}` | 6 | Tier 3 |
| `escaped-json` — `{\"a\": \"b\"}` | 6 | Tier 4 |
| `missing-comma-structure` | 6 | Tier 3 |
| `jsonp-callback` — `cb({...})` | 5 | Tier 4 |
| `string-concat` — `"a" + "b"` | 5 | Tier 4 |
| `comment-string-concat` | 3 | Tier 4 |
| `mongodb-functions` — `ISODate(...)` | 3 | Tier 4 |
| `regex-literal` — `/pattern/` | 3 | Tier 4 |
| `truncated-unicode-escape` — `"\uD8"` | 2 | Tier 3 |
| `other` | 1 | — |
| `upstream-trap` (upstream test artifact) | 1 | — |

### Behavioural cross-checks

Where the intended repair was ambiguous, both upstreams were installed at the pinned versions
above and run directly rather than reasoned about. Recorded so the comparison is reproducible:

| Input | josdejong `v3.15.0` | mangiucugna `v0.61.7` | JsonRepair.NET 0.2.0 |
| :--- | :--- | :--- | :--- |
| `8 67` | throws | `""` (its failure value) | throws |
| `-2 7.241` | throws | `""` | throws |
| `[8 67]` | `[8, 67]` | `[8, 67]` | `[8, 67]` |
| `{"a": 8 67}` | throws | `{"a": 8}` | throws |
| `n ull` | `"n ull"` | `""` | throws |
| `{"a": n ull}` | `{"a": "n ull"}` | `{"a": "n ull"}` | throws |
| `536,` | `536` | `""` | `536` (UTF-8) / throws (string) |

**Neither upstream ever fuses tokens.** They insert a separator, quote the run as a string, or
refuse. This settled a defect found by the fuzz harness in 0.2.0, where the UTF-8 engine repaired
`8 67` to `867` — valid JSON carrying a number that was never in the input. Both engines now
reject it, guarded by `EngineAgreementTests.NeitherEngine_ShouldFuseWhitespaceSplitTokens`.

Where upstream has a better answer than refusing — NDJSON wrapping for `-8\n93`, unquoted-value
quoting for `n ull` — that is Tier 3/4 work. Refusing is the safe subset: never invented data.

### Known engine divergences

The string and UTF-8 engines are separate implementations and do not yet agree everywhere:

- **UTF-8 more lenient:** a trailing comma after a root-level primitive (`536,` → `536`). The
  UTF-8 side matches josdejong here; the string engine rejects it. Closes with Tier 3.
- **String more lenient:** the 6 `special-whitespace` corpus cases (`JS210`, `JS212`, `JS213`,
  `JS215`, `JS216`, `JS217`), where the string engine repairs non-ASCII whitespace such as NBSP
  and U+3000 and the UTF-8 engine rejects it. These are the 6-case gap between the 242 baseline
  entries and the 236 that currently fail the string engine.

Both are incompleteness, not fabrication. `Fuzz_BothEnginesShouldAgree` enforces that any *other*
disagreement fails the build; it does not currently generate non-ASCII whitespace, which is why
the second class came from the corpus rather than from fuzzing.

---

## Synchronization Protocol

To ensure `JsonRepair.NET` maintains full compatibility as upstream libraries evolve:

1. **Automated Release Watcher**: A GitHub Action (`.github/workflows/upstream-watch.yml`) polls the GitHub API for new tags/releases on **both** upstream repositories weekly.
   (Note: `josdejong/jsonrepair` publishes tags only, no GitHub Releases — the watcher polls `/tags`.)
2. **Upstream Test Vector Sync**: The josdejong suite is ported wholesale (see Measured Subset above). The mangiucugna corpus is **not yet ported** — it is required by 1.0.0 gate criterion 2.
3. **Change Log Matrix**:

| Date | Target Version | Upstream Version | Sync Status | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **2026-07-23** | `v0.1.0` | `josdejong/jsonrepair@v3.15.0` | ⚠️ Subset | Core syntax repairs implemented; full repair-category parity tracked in [05-pre-1.0-roadmap.md](05-pre-1.0-roadmap.md) (Tiers 2–4) |
| **2026-07-25** | `v0.1.0` | `mangiucugna/json_repair@v0.61.7` | ⚠️ Subset | Pins corrected (previously phantom `v5.1.0` / stale `v0.30.0`); strict mode, schema-guided repair, `stream_stable` scheduled for Tiers 4–5 |
| **2026-08-28** | `v0.2.0` | `josdejong/jsonrepair@v3.15.0` | ⚠️ Subset — **measured** | Corpus ported and running against both engines: 191/427 (44.7%), 242 categorised gaps. Replaces the previous unquantified "Subset" claim |
| **2026-08-28** | `v0.2.0` | `mangiucugna/json_repair@v0.61.7` | ⚠️ Subset — behaviour cross-checked | Corpus not ported. Run directly to settle token-fusion semantics (see Behavioural cross-checks) |
