# AutomateX v5.0.0 — multi-node / HA

AutomateX can now run as **several API replicas** behind a load balancer, with **Postgres as the only
stateful backbone**. This retires the deliberate single-node design (`DurabilityMode.Solo` + the
one-container hosting story). It's **opt-in**: a single-node deploy on Solo with no Redis behaves exactly
as it did before v5.0 — no breaking change.

Full notes: [CHANGELOG.md](CHANGELOG.md) · design: [docs/multi-node-design.md](docs/multi-node-design.md) ·
HA guide: [docs/deploy-homelab.md](docs/deploy-homelab.md) (High availability section).

## Highlights

- **Clustered execution.** `Engine:DurabilityMode=Balanced` turns on Wolverine leader election and routes
  runs onto a shared Postgres queue that every node pulls from as competing consumers — executions
  distribute across the cluster, with automatic failover.
- **Singletons run once.** The cron scheduler, plugin-trigger host, and the stuck/retention sweepers become
  Wolverine agents assigned to exactly one node, so triggers fire once, not once-per-replica. The per-node
  plugin-host supervisors still run everywhere (they police each node's own plugin processes).
- **Live UI across nodes.** An opt-in SignalR **Redis backplane** (`ConnectionStrings:redis`) fans execution
  events and plugin logs out to clients on every node.
- **Cross-node plugin reload.** Upload / install / enable on one node propagates to peers over Postgres
  `LISTEN/NOTIFY`, so every node recycles its plugin hosts.
- **Safe concurrent boot.** The startup migration is advisory-locked, so N nodes booting at once don't race
  `MigrateAsync`.
- **Postgres-only backbone.** No new broker — Redis is added purely for SignalR fan-out. No new EF
  migration; Wolverine self-provisions its cluster/queue schema.

## Upgrading

**Single node (almost everyone): nothing changes.** Pull the refreshed `docker-compose.prod.yml` — it pins
`DURABILITY_MODE=Solo`, the same fast in-process behaviour as before. If you keep an older compose file that
doesn't set `Engine__DurabilityMode`, set it to `Solo` explicitly, since the app itself now defaults to
Balanced.

**Going HA** is a deliberate, four-part switch — do them together (details in the deploy guide):

1. `.env`: `DURABILITY_MODE=Balanced` and `REDIS_CONNECTION=redis:6379`.
2. `Caddyfile`: switch the `/hubs` block to the dynamic-upstream + cookie-stickiness form.
3. Bring up with `docker compose -f docker-compose.prod.yml --profile ha up -d --scale api=2`.
4. Set per-step **idempotency keys** on side-effecting steps — delivery is at-least-once and a node dying
   mid-step can redeliver.

> ⚠ Running more than one replica on **Solo** double-fires triggers — Balanced is required whenever `api`
> has more than one replica. Replicas currently must share one Docker host (they share the `automatex-plugins`
> volume); cross-host shared storage is deferred.
