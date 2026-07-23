# Upstream Source Tracking & Synchronization Log

`JsonRepair.NET` is a clean-room, native .NET 10 implementation inspired by the algorithms and test cases of the following open-source projects:

## Upstream References

| Upstream Project | Primary Author | Language | License | Tracked Repository | Pinned Version / Commit |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`jsonrepair`** | Jos de Jong | JavaScript | ISC | [josdejong/jsonrepair](https://github.com/josdejong/jsonrepair) | `v5.1.0` |
| **`json_repair`** | Mangiucugna | Python | MIT | [mangiucugna/json_repair](https://github.com/mangiucugna/json_repair) | `v0.30.0` |

---

## Synchronization Protocol

To ensure `JsonRepair.NET` maintains full compatibility as upstream libraries evolve:

1. **Automated Release Watcher**: A GitHub Action (`.github/workflows/upstream-watch.yml`) polls the GitHub API for new release tags on upstream repositories weekly.
2. **Upstream Test Vector Sync**: The test matrix in `JsonRepair.Tests` includes test cases adapted directly from the upstream `josdejong/jsonrepair` unit test suite.
3. **Change Log Matrix**:

| Date | Target Version | Upstream Version | Sync Status | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **2026-07-23** | `v1.0.0` | `josdejong/jsonrepair@v5.1.0` | ✅ Fully Synced | Initial .NET 10 release with 100% parity |
