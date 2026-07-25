# Upstream Source Tracking & Synchronization Log

`JsonRepair.NET` is a clean-room, native .NET 10 implementation inspired by the algorithms and test cases of the following open-source projects:

## Upstream References

| Upstream Project | Primary Author | Language | License | Tracked Repository | Pinned Version / Commit |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`jsonrepair`** | Jos de Jong | JavaScript | ISC | [josdejong/jsonrepair](https://github.com/josdejong/jsonrepair) | `v3.15.0` |
| **`json_repair`** | Mangiucugna | Python | MIT | [mangiucugna/json_repair](https://github.com/mangiucugna/json_repair) | `v0.61.7` |

---

## Synchronization Protocol

To ensure `JsonRepair.NET` maintains full compatibility as upstream libraries evolve:

1. **Automated Release Watcher**: A GitHub Action (`.github/workflows/upstream-watch.yml`) polls the GitHub API for new tags/releases on **both** upstream repositories weekly.
   (Note: `josdejong/jsonrepair` publishes tags only, no GitHub Releases — the watcher polls `/tags`.)
2. **Upstream Test Vector Sync**: The test matrix in `JsonRepair.Tests` includes test cases adapted directly from the upstream `josdejong/jsonrepair` unit test suite.
3. **Change Log Matrix**:

| Date | Target Version | Upstream Version | Sync Status | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **2026-07-23** | `v0.1.0` | `josdejong/jsonrepair@v3.15.0` | ⚠️ Subset | Core syntax repairs implemented; full repair-category parity tracked in [05-pre-1.0-roadmap.md](05-pre-1.0-roadmap.md) (Tiers 2–4) |
| **2026-07-25** | `v0.1.0` | `mangiucugna/json_repair@v0.61.7` | ⚠️ Subset | Pins corrected (previously phantom `v5.1.0` / stale `v0.30.0`); strict mode, schema-guided repair, `stream_stable` scheduled for Tiers 4–5 |
