# Golem sample (Unity + Go)

Server-authoritative Golem sample: a Go WebTransport server and a Unity 6 client. Entity schemas and catalogs are authored in Unity via Scribe; monsters are data-driven; movement and monster AI run on the server tick; scene-placed decor is exported to SQLite and streamed as Object Template collision (server) and visuals (client).

**Open the `unity/` subfolder in Unity Hub.** The repository root is the Go server workspace; opening it in Unity creates an empty project without the sample scene or Golem menu. The client scene is `unity/Assets/Scenes/SampleScene.unity`.

## Prerequisites

| Piece | Notes |
| --- | --- |
| **Unity 6** | Project version `6000.3.13f1` (`unity/ProjectSettings/ProjectVersion.txt`). |
| **Local `golem-engine`** | Checkout at `../../golem-engine` (the sample lives under `golem-samples/golem-sample-unity`). Go `replace` directives in `go.mod` point there. Unity consumes the built UPM at `file:…/golem-engine/artifacts/upm/io.demiurgos.golemengine` (absolute path in `unity/Packages/manifest.json`). |
| **Local `webtransport-csharp`** | Checkout alongside `golem-engine`; the Unity manifest points to its built `io.demiurgos.webtransport` package. |
| **Go** | Version from `go.mod` (currently `1.27.1`). |

This sample uses local engine checkouts. If you use a different directory layout, update the Go replacement paths, Unity package paths, and **Golem > Settings > Project Root** (the folder containing `golem.yaml`).

## Repository layout

```
cmd/server/           Go server entrypoint
internal/gameplay/    Intent store, monster AI, FOI/spawn constants
internal/world/       SQLite world DB reader, decor streamer, movement slide
internal/synced/      Bake output (generated — do not hand-edit)
schemas/              Scribe-owned entity/type/world YAML (+ handwritten commands/events)
catalogs/             Scribe-owned catalog data YAML
object-templates.json Scribe-owned Object Templates (prefab GUID IDs)
scribe.golem.yaml     Scribe manifest (generated)
golem.yaml            Bake / integration paths
unity/Assets/Game/    Authored game content + runtime/editor C# (Game.Runtime / Game.Editor)
unity/Assets/Golem/   Codegen: Generated/ (do not edit) + Views/ (edit-as-needed; this sample subclasses instead)
unity/Assets/StreamingAssets/world.db   Committed decor database (Git LFS)
```

Assembly split:

- `Game.Runtime` — `unity/Assets/Game/` gameplay scripts
- `Game.Editor` — `unity/Assets/Game/Editor/` (Editor-only)
- `Golem.Generated` — `unity/Assets/Golem/{Generated,Views}`

## What it demonstrates

1. **Scribe-owned entity schemas** — `[GolemEntity]` / `[GolemVar]` on `entity_Player.prefab` and `entity_Monster.prefab` export to `schemas/entities/`.
2. **Data-driven monsters** — `MonsterType` `[GolemCatalog]` assets (`GoblinGuard`, `Orc`) plus a Unity-only `MonsterList` for id→visual lookup. Export writes `schemas/types/`, `schemas/world/`, and `catalogs/monster_type.golem.yaml`.
3. **Server-authoritative movement** — latest `Move` is standing intent (normalized, hostile input sanitized), integrated each tick at units/second with ~250 ms expiry. Session event `LocalPlayerAssigned` tells each client which entity it controls.
4. **Client** — bounded-rate intent (~30 Hz) with one zero-vector send on release; damped camera follow; time-based snapshot interpolation (no prediction).
5. **Monster AI + interest** — wander near home, chase nearby players at catalog `MoveSpeed`; `CellSize` enables per-session FOI.
6. **Decor (not entities)** — the wall prefab carries `GolemObjectTemplate` → `object-templates.json` (non-blocking pavement has no template); `DecorType` catalog (id, name, blocking, prefab GUID); scene placements exported via **Game > World > Export World Database** to `world.db`. Server streams blocking rows into a resolv collision backend and blocks/slides movement; client streams the same rows as visuals.

## How to run

**Server** (from repo root, with `../../golem-engine` present):

```bash
go run ./cmd/server
```

Listens on `https://localhost:8080/wt` with a dev self-signed cert. Optional flags: `-world-db`, `-object-templates`, `-decor-catalog` (defaults resolve from the repo root via source path, not cwd). `-world-probe` runs an in-process streaming/blocking check and exits.

**Client:** open `unity/` in Unity 6, play `Assets/Scenes/SampleScene.unity` (URL is `https://localhost:8080/wt`, untrusted certs allowed). Start the server first.

## Authoring loop

1. Edit Unity sources: entity prefabs / authoring components, `MonsterType` / `DecorType` assets, template-marked decor prefabs, or scene `DecorInstance` placements.
2. **Golem > Scribe > Export All** — regenerates Scribe YAML (`schemas/`, `catalogs/`, `object-templates.json`, `scribe.golem.yaml`) and typically triggers bake into `internal/synced/` and `unity/Assets/Golem/{Generated,Views}/`.
3. If the decor scene changed: **Game > World > Export World Database**.
4. Commit the generated artifacts (including `scribe.golem.yaml` and `world.db` when applicable).
5. Restart / run the Go server and play the client.

Dry-run check without writing: **Golem > Scribe > Validate**.

Batch-mode CI entry: dry-run validates, then runs Export All (which writes Scribe YAML), then validates again. It exits nonzero when sources/artifacts are invalid **or when Export All changed bytes** (committed artifacts were stale). An automatic repair during that run does not count as success — run **Golem > Scribe > Export All** locally and commit the result, then re-run CI.

```bash
Unity -batchmode -projectPath unity -executeMethod GolemEngine.Unity.Editor.GolemScribeCI.ExportAllAndValidate -logFile -
```

Do not run that batch-mode command while this project’s Editor is already open (asset-database lock).

### Do not hand-edit

- `schemas/entities/`, `schemas/types/`, `schemas/world/` (Scribe)
- `catalogs/*.golem.yaml`, `object-templates.json`, `scribe.golem.yaml`
- `internal/synced/`
- `unity/Assets/Golem/Generated/`

`unity/Assets/Golem/Views/` is generated with an “edit as needed” header; bake may overwrite local edits. This sample keeps customization in `Game.Runtime` subclasses (`PlayerVisual`, `MonsterVisual`) instead of editing Views in place. Handwritten command/event schemas under `schemas/commands/` and `schemas/events/` are intentional exceptions.

## Adding a third monster type

No new entity prefab, no new C# type, and no hand-edited schema:

1. Add a visual prefab under `unity/Assets/Game/Monsters/Visuals/` (sprite only).
2. **Create > Golem Sample > Monster Type** — set Name, Health, Damage, MoveSpeed, and Visual.
3. Select `MonsterList`, click **Refresh** in the inspector (picks up the asset and syncs ids).
4. **Golem > Scribe > Export All**, commit the catalog/schema/bake output.
5. Restart the server so it reloads `catalogs/monster_type.golem.yaml`.

The client resolves visuals through `MonsterList` automatically. This sample’s spawn table (`internal/gameplay/monster.go` → `SampleMonsterSpawns`) only references `TypeIndex` 0 and 1 today; to place the new type in the world, add a spawn line with `TypeIndex: 2` (sorted catalog order). Defining the type itself still needs no entity prefab, schema, or C# change.

## CI

Workflow: [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

| Job | When | What |
| --- | --- | --- |
| **Go** | push / PR / dispatch | Checks out this repo and sibling `golem-engine`, then `go build ./...`, `go vet ./...`, `go test -count=1 ./...`. |
| **Scribe** | `workflow_dispatch` with **run_scribe** only | Always attempts `GolemScribeCI.ExportAllAndValidate`. If no Unity Editor is found on the runner, the job **exits 1** with an error (it does not detect-and-skip). |

Scribe is gated (not on every PR) because: (1) it needs a licensed Editor, and (2) Unity package dependencies use absolute local `file:` paths that GitHub-hosted runners cannot resolve. Use **Golem > Scribe > Validate** locally. Set repo variables `SCRIBE_RUNS_ON` / `UNITY_EDITOR_PATH` (and package layout) if you wire a self-hosted runner later.

Go CI needs access to `demiurgos-hub/golem-engine` (or `vars.GOLEM_ENGINE_REPO`); private clones require `secrets.GOLEM_ENGINE_TOKEN`.

## Known limitations

- **WebTransport session instability (observed):** the client session is accepted, then often drops after a few seconds with `stream closed before reliable frame completed`. That has blocked end-to-end play-mode checks of movement, camera follow, and wall collision in this environment. The local setup uses `DevSelfSignedCert` on the server and `AllowUntrustedCertificates` on the client. Root cause is not established.
- **SQLite via StreamingAssets** is desktop/Editor-oriented. Mobile/WebGL need a copy to `persistentDataPath` before opening; this sample does not implement that path.
- **`go test -race`** needs cgo/gcc; CI and the documented commands use `go test -count=1 ./...` without `-race`.
- Catalog id fields are **`int` / `int32`**, not `uint` / `uint32`, because C# bake emits an untyped `0` literal for `uint32` map keys (`var key = 0`).
- Generated `internal/synced/entities.proto` emits typeless `items` fields on catalog world-map messages (`DecorTypeData` / `MonsterTypeData`). Go and C# bake outputs are fine; the `.proto` is not consumable by `protoc` as-is.
