# AutomateX v5.2.0 — plugin catalog overhaul

Plugins become a first-class, **browsable, shareable catalog** — independently versioned, cryptographically
trusted, and permission-aware — decoupled from the API's release train. First-party plugins now live in their
own open-source repo, [AutomateX-Plugins](https://github.com/Eiromplays/AutomateX-Plugins), with their own
semver and a signed release catalog; the engine consumes it as one of any number of operator-configurable
sources.

Full notes: [CHANGELOG.md](CHANGELOG.md) · design:
[docs/plugin-catalog-design.md](docs/plugin-catalog-design.md) · authoring:
[docs/recipes/publishing-plugins.md](docs/recipes/publishing-plugins.md).

## Highlights

- **Independent plugin versioning.** Each plugin ships a `plugin.json` with its own semver, bumped only when
  the plugin changes — no more phantom updates that just track the API version. The SDK + Protocol are on
  NuGet so anyone can build a plugin out of tree.
- **Multi-source, signed catalog.** Add/trust/remove catalog sources at runtime (public, third-party, or a
  private LAN host). Each catalog is **minisign-signed**; sources pin a publisher key, and installs are
  gated by trust — an untrusted source needs an instance admin + explicit acknowledgement. sha256 still
  verifies every download before it touches disk.
- **Enforced permissions.** A plugin declares the network egress it needs; it's shown for consent at install
  and enforced at runtime — the plugin can reach only what it declared.
- **Opt-in auto-update.** Keep a plugin on the latest compatible, same-major version from a verified source.
  Off by default; never crosses a major or trusts an unverified source.
- **New Plugins UI.** Searchable/filterable catalog with trust badges, a detail view with version picker,
  permission consent, an auto-update toggle, and source management.

## Upgrading

- **Two EF migrations** ship (`PluginCatalog`, `PluginAutoUpdate`) — applied automatically when
  `Database:MigrateOnStartup` is on, else `dotnet ef database update`.
- **Nothing else is required.** With no extra sources, signatures, or permissions configured, behaviour
  matches pre-v5.2. `Engine__PluginCatalogUrl` now defaults to the AutomateX-Plugins release catalog; point
  it elsewhere (or add sources in **Plugins → Catalog sources**) to use your own.
- **Building plugins locally** now means cloning
  [AutomateX-Plugins](https://github.com/Eiromplays/AutomateX-Plugins) (or your own plugin repo) — they're no
  longer part of `src/Plugins`. The runtime, SDK, and the `AutomateX.SamplePlugin` reference plugin stay in
  this repo.
- **Trusting the first-party catalog** requires the AutomateX release public key pinned in
  `CatalogKeys.FirstParty`; until a catalog is signed, its source shows as Untrusted (installs still gated by
  `AllowPluginUpload`, as before).
