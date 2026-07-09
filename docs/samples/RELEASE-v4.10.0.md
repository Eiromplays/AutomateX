# AutomateX v4.10.0

A focused minor closing the long-standing `forEach` follow-up: loop children now run in **bounded
parallel** instead of strictly one at a time. One migration (a per-item results table).

## Highlights

- **`forEach` runs items in parallel, capped.** A `forEach` step took its array one item at a time. A
  new **`concurrency`** (default `1` = unchanged) runs up to N children at once, clamped to
  `[1, min(itemCount, Engine:ForEachMaxConcurrency)]` (ceiling default `20`, so a large array can't fan
  out unbounded child runs). Set it in the builder's forEach editor. Results are still collected **in
  item order** and per-item failures still land in their own slot — only the number in flight changes.
- **A faster, contention-free accumulator.** Under the hood the loop's result store was rebuilt. The old
  single `ResultsJson` blob re-serialized the whole array on every completion (O(N²)) and was a single
  row every parallel child would contend on. Each item now has its own row in a new `ForEachItemResults`
  table: a child writes only its own row (idempotent via the unique `(ExecutionId, StepOrder, ItemIndex)`
  key), counters advance with atomic `UPDATE`s, and a single finisher is elected lock-free — so
  concurrent completions never lose a result, double-run an item, or complete the loop twice.

## Upgrade notes

- **Schema change (its migration landed in v4.11).** The rebuild creates the `ForEachItemResults` table
  (+ its unique index), adds `ForEachState.Concurrency`, and drops the now-unused
  `ForEachState.ResultsJson`/`NextIndex` columns. This shipped **without its own migration** in v4.10 —
  the test suite builds the schema straight from the model (`EnsureCreated`), so it went unnoticed — and
  the change was captured in v4.11's combined `AddApiKeyGuards` migration. A deploy crossing v4.10 → v4.11
  applies it there; both migrations run automatically on startup (`Database:MigrateOnStartup`, default on)
  or via `dotnet ef database update`.
- **`concurrency = 1` is byte-for-byte the previous behaviour.** Existing workflows are untouched; the
  cap is opt-in per step.
- **Config:** `Engine:ForEachMaxConcurrency` (default `20`) bounds how parallel any one loop can get.
- **Drain in-flight loops before upgrading.** The dropped columns hold a running loop's transient
  accumulator; a `forEach` suspended across the deploy would lose its progress. On a single-node deploy,
  let active loops finish (or expect to re-run any caught mid-flight) — nothing else is affected.

## Known follow-ups

- The `forEach` capped-parallel follow-up (tracked since v3.4) is now closed.
- Next queued loose-end: **per-key API rate limits / IP allowlists** (deferred from v4.6) — its own
  focused, auth-reviewed release.

See the [forEach concurrency design](docs/foreach-concurrency-design.md) and the
[sub-workflows & loops recipe](docs/recipes/sub-workflows-and-loops.md). Full history:
[CHANGELOG.md](CHANGELOG.md).
