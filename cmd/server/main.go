package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"log"
	"math"
	"math/rand"
	"os"
	"os/signal"
	"sort"
	"syscall"
	"time"

	"github.com/demiurgos-hub/golem-engine/golem"
	"github.com/demiurgos-hub/golem-engine/golem/collision"
	collisionresolv "github.com/demiurgos-hub/golem-engine/golem/collision/resolv"
	"github.com/demiurgos-hub/golem-engine/golem/objecttemplate"
	"golem-sample-unity/internal/gameplay"
	"golem-sample-unity/internal/synced"
	"golem-sample-unity/internal/world"
)

func main() {
	worldDBPath := flag.String("world-db", world.DefaultWorldDBPath(),
		"read-only path to StreamingAssets/world.db (default resolved from repo root via source path, not cwd)")
	templatesPath := flag.String("object-templates", world.DefaultObjectTemplatesPath(),
		"path to object-templates.json")
	decorCatalogPath := flag.String("decor-catalog", world.DefaultDecorTypeCatalogPath(),
		"path to catalogs/decor_type.golem.yaml")
	worldProbe := flag.Bool("world-probe", false,
		"run in-process decor streaming/blocking probe then exit (no listen)")
	flag.Parse()

	if *worldDBPath == "" || *templatesPath == "" || *decorCatalogPath == "" {
		rootErr := ""
		if _, err := world.RepoRoot(); err != nil {
			rootErr = err.Error()
		}
		log.Fatalf("empty world asset path(s): world-db=%q object-templates=%q decor-catalog=%q; %s",
			*worldDBPath, *templatesPath, *decorCatalogPath, rootErr)
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	srv, rt := synced.NewServer(golem.ServerConfig{
		TickRate:          20,
		Addr:              ":8080",
		Transport:         golem.TransportWebTransport,
		StateUpdateLane:   golem.StateUpdateLaneDatagram,
		DevSelfSignedCert: true,
		// CellSize > 0 enables the spatial hash and per-session FOI filtering.
		// 8 units ≈ 2s of player travel at PlayerMoveSpeed; FOI radius 15 keeps
		// nearby catalog spawns visible while far spawns start outside interest.
		CellSize: gameplay.InterestCellSize,
	})

	// resolv: detection-only. Blocking uses OverlapCircle + ResolveSlide so the
	// sample's per-axis slide stays visible (Chipmunk would own resolution).
	backend := collisionresolv.New()
	srv.SetCollisionBackend(backend)
	layers := world.SetupLayers(backend)
	decorMask := layers.Layer(world.LayerDecorCollision)

	streamer, err := openWorldStreamer(*worldDBPath, *templatesPath, *decorCatalogPath, backend, layers)
	if err != nil {
		log.Fatal(err)
	}
	defer func() { _ = streamer.DB.Close() }()

	if *worldProbe {
		if err := runWorldProbe(streamer, backend, decorMask); err != nil {
			log.Fatal(err)
		}
		return
	}

	intents := gameplay.NewIntentStore()
	// Seeded RNG keeps wander reproducible across sample runs; only touched from OnTick.
	monsterAI := gameplay.NewMonsterAI(rand.New(rand.NewSource(1)))

	if err := loadAndSpawnMonsters(srv); err != nil {
		log.Fatal(err)
	}

	playerBySession := make(map[int64]int64)

	srv.OnConnect(func(sess *golem.Session) {
		player := synced.NewSyncedPlayer(0, 0, 0)
		if err := srv.CreateEntity(player, sess.ID); err != nil {
			log.Printf("create player for session %d: %v", sess.ID, err)
			return
		}
		playerBySession[sess.ID] = player.EntityID()
		// Interest is enabled (CellSize > 0): assign FOI so the client receives
		// nearby entities on the interest tick. World-data snapshots still go out
		// on connect via the listener; only the entity snapshot is deferred.
		srv.AssignFOI(sess.ID, player.EntityID(), gameplay.FOIRadius, gameplay.FOIMargin)
		if err := rt.Events.SendLocalPlayerAssigned(sess.ID, player.EntityID()); err != nil {
			log.Printf("send LocalPlayerAssigned to session %d: %v", sess.ID, err)
		} else {
			log.Printf("session %d connected, player entity %d spawned, LocalPlayerAssigned sent, FOI assigned", sess.ID, player.EntityID())
		}
	})

	srv.OnDisconnect(func(sess *golem.Session) {
		playerID, ok := playerBySession[sess.ID]
		if !ok {
			return
		}
		delete(playerBySession, sess.ID)
		intents.Clear(playerID)
		srv.RemoveFOI(sess.ID)
		srv.DeleteEntity(playerID)
		log.Printf("session %d disconnected, player entity %d removed", sess.ID, playerID)
	})

	rt.Commands.OnMove(func(senderID int64, player *synced.SyncedPlayer, cmd *synced.MoveCommand) {
		_ = senderID
		intents.Record(player.EntityID(), float64(cmd.Dx), float64(cmd.Dy), time.Now())
	})

	overlaps := func(x, y, radius float64) bool {
		// Hits are synthetic negative placer IDs for decor — never treat as entity IDs.
		return len(srv.OverlapCircle(x, y, radius, decorMask)) > 0
	}

	srv.OnTick(func(dt float64, s *golem.Server) {
		// Fatal: a soft log would leave residency stale/empty and silently disable walls.
		if err := streamer.Tick(streamAnchors(s)); err != nil {
			log.Fatalf("world streamer tick failed (collision residency unsafe): %v", err)
		}

		active := intents.Active(time.Now(), gameplay.IntentExpiry)
		for entityID, intent := range active {
			ent, ok := s.Get(entityID)
			if !ok {
				intents.Clear(entityID)
				continue
			}
			player, ok := ent.(*synced.SyncedPlayer)
			if !ok {
				continue
			}
			x, y := player.Position()
			nx, ny := world.ResolveSlide(
				float64(x), float64(y),
				intent.DX*gameplay.PlayerMoveSpeed*dt,
				intent.DY*gameplay.PlayerMoveSpeed*dt,
				world.ActorRadius,
				overlaps,
			)
			player.SetPosition(float32(nx), float32(ny))
		}

		speedByType := monsterSpeeds(s)
		monsters, players := collectActors(s, speedByType)
		for _, d := range monsterAI.Step(dt, monsters, players) {
			ent, ok := s.Get(d.EntityID)
			if !ok {
				monsterAI.Forget(d.EntityID)
				continue
			}
			monster, ok := ent.(*synced.SyncedMonster)
			if !ok {
				continue
			}
			cx, cy := monster.Position()
			nx, ny := world.ResolveSlide(
				float64(cx), float64(cy),
				d.X-float64(cx),
				d.Y-float64(cy),
				world.ActorRadius,
				overlaps,
			)
			monster.SetPosition(float32(nx), float32(ny))
		}
	})

	rt.Commands.BindAllHandlers(func(sess *golem.Session, err error) {
		log.Printf("command error from session %d: %v", sess.ID, err)
	})

	log.Printf(
		"golem sample server listening on https://localhost:8080/wt (CellSize=%.0f FOIRadius=%.0f worldDB=%s)",
		gameplay.InterestCellSize, gameplay.FOIRadius, *worldDBPath,
	)
	if err := srv.Run(ctx); err != nil && !errors.Is(err, context.Canceled) {
		log.Fatal(err)
	}
}

func openWorldStreamer(
	dbPath, templatesPath, decorCatalogPath string,
	backend collision.Backend,
	layers *collision.Layers,
) (*world.Streamer, error) {
	db, err := world.OpenDB(dbPath)
	if err != nil {
		return nil, err
	}
	templates, _, err := objecttemplate.DecodeFile(templatesPath, objecttemplate.DefaultLimits())
	if err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("load object templates %s: %w", templatesPath, err)
	}
	catalog, err := synced.LoadDecorTypeData(decorCatalogPath)
	if err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("load DecorType catalog %s: %w", decorCatalogPath, err)
	}
	placer := &objecttemplate.Placer2D{
		Backend: backend,
		Catalog: templates,
		Resolve: objecttemplate.LayersResolver(layers),
	}
	return world.NewStreamer(db, catalog, placer, world.StreamRadius), nil
}

// streamAnchors centers residency on every player and monster so AI cannot walk
// through unloaded walls outside player StreamRadius. Cost: one cheap bounds
// query per living actor per tick; far monsters over empty map regions place
// nothing, so the authored enclosure is not loaded globally.
func streamAnchors(s *golem.Server) []world.Anchor {
	var out []world.Anchor
	for _, ent := range s.All() {
		switch e := ent.(type) {
		case *synced.SyncedPlayer:
			x, y := e.Position()
			out = append(out, world.Anchor{X: float64(x), Y: float64(y)})
		case *synced.SyncedMonster:
			x, y := e.Position()
			out = append(out, world.Anchor{X: float64(x), Y: float64(y)})
		}
	}
	return out
}

func runWorldProbe(streamer *world.Streamer, backend *collisionresolv.ResolvBackend, decorMask uint32) error {
	overlaps := func(x, y, radius float64) bool {
		return len(backend.OverlapCircle(x, y, radius, decorMask)) > 0
	}

	if err := streamer.Tick([]world.PlayerPos{{X: 0, Y: 0}}); err != nil {
		return err
	}
	st := streamer.Stats()
	log.Printf("world-probe: enter place=%d remove=%d resident=%d", st.PlaceCalls, st.RemoveCalls, st.Resident)
	if st.Resident == 0 {
		return fmt.Errorf("world-probe: no blocking decor placed near origin")
	}

	rows, err := streamer.DB.QueryAll()
	if err != nil {
		return err
	}
	var wall world.DecorRow
	for _, r := range rows {
		if r.DecorTypeId == 1 {
			wall = r
			break
		}
	}
	startX, startY := wall.PosX, wall.PosY-1.2
	if overlaps(startX, startY, world.ActorRadius) {
		startX, startY = wall.PosX, wall.PosY+1.2
	}
	step := gameplay.PlayerMoveSpeed * 0.05 // fixed tick dt
	x, y := startX, startY
	dir := 1.0
	if startY > wall.PosY {
		dir = -1.0
	}
	for i := 0; i < 40; i++ {
		x, y = world.ResolveSlide(x, y, 0, dir*step, world.ActorRadius, overlaps)
	}
	log.Printf("world-probe: clamp against wall@(%g,%g) -> player=(%g,%g) overlapping=%v nextYBlocked=%v",
		wall.PosX, wall.PosY, x, y, overlaps(x, y, world.ActorRadius),
		overlaps(x, y+dir*step, world.ActorRadius))

	sx, sy := world.ResolveSlide(x, y, step, dir*step, world.ActorRadius, overlaps)
	log.Printf("world-probe: diagonal slide from clamp -> (%g,%g) dyHeld=%v dxMoved=%v overlapping=%v",
		sx, sy, sy == y, sx != x, overlaps(sx, sy, world.ActorRadius))

	if err := streamer.Tick([]world.PlayerPos{{X: 500, Y: 500}}); err != nil {
		return err
	}
	st2 := streamer.Stats()
	log.Printf("world-probe: leave place=%d remove=%d resident=%d balanced=%v",
		st2.PlaceCalls, st2.RemoveCalls, st2.Resident, st2.PlaceCalls == st2.RemoveCalls && st2.Resident == 0)
	if st2.Resident != 0 || st2.PlaceCalls != st2.RemoveCalls {
		return fmt.Errorf("world-probe: place/remove unbalanced: %+v", st2)
	}
	return nil
}

func monsterSpeeds(s *golem.Server) map[uint32]float64 {
	out := make(map[uint32]float64)
	raw := s.World.Get("MonsterType")
	if raw == nil {
		return out
	}
	catalog, ok := raw.(*synced.MonsterTypeData)
	if !ok || catalog == nil {
		return out
	}
	for id, row := range catalog.Items {
		out[uint32(id)] = float64(row.MoveSpeed)
	}
	return out
}

func collectActors(s *golem.Server, speedByType map[uint32]float64) ([]gameplay.MonsterView, []gameplay.PlayerView) {
	var monsters []gameplay.MonsterView
	var players []gameplay.PlayerView
	for _, ent := range s.All() {
		switch e := ent.(type) {
		case *synced.SyncedMonster:
			x, y := e.Position()
			monsters = append(monsters, gameplay.MonsterView{
				EntityID: e.EntityID(),
				Pos:      gameplay.Vec2{X: float64(x), Y: float64(y)},
				TypeID:   e.MonsterTypeId(),
				Speed:    speedByType[e.MonsterTypeId()],
			})
		case *synced.SyncedPlayer:
			x, y := e.Position()
			players = append(players, gameplay.PlayerView{
				Pos: gameplay.Vec2{X: float64(x), Y: float64(y)},
			})
		}
	}
	return monsters, players
}

func loadAndSpawnMonsters(srv *golem.Server) error {
	const catalogPath = "catalogs/monster_type.golem.yaml"

	catalog, err := synced.LoadMonsterTypeData(catalogPath)
	if err != nil {
		return fmt.Errorf("load MonsterType catalog from %s: %w", catalogPath, err)
	}
	if catalog == nil || len(catalog.Items) == 0 {
		return fmt.Errorf("MonsterType catalog %s is missing or empty", catalogPath)
	}

	srv.World.Set(catalog)
	log.Printf("loaded MonsterType catalog (%d types) from %s", len(catalog.Items), catalogPath)

	ids := make([]int32, 0, len(catalog.Items))
	for id := range catalog.Items {
		ids = append(ids, id)
	}
	sort.Slice(ids, func(i, j int) bool { return ids[i] < ids[j] })

	for _, sp := range gameplay.SampleMonsterSpawns {
		if sp.TypeIndex < 0 || sp.TypeIndex >= len(ids) {
			return fmt.Errorf("spawn TypeIndex %d out of range for %d catalog types", sp.TypeIndex, len(ids))
		}
		id := ids[sp.TypeIndex]
		row, ok := catalog.Items[id]
		if !ok {
			return fmt.Errorf("MonsterType catalog missing row id %d", id)
		}
		if row.Id <= 0 {
			return fmt.Errorf("MonsterType catalog row has invalid id %d (name=%q); id must be > 0", row.Id, row.Name)
		}

		monster := synced.NewSyncedMonster(float32(sp.X), float32(sp.Y), uint32(row.Id), row.Health)
		if err := srv.CreateEntity(monster); err != nil {
			return fmt.Errorf("spawn monster type %d (%s): %w", row.Id, row.Name, err)
		}
		dist := math.Hypot(sp.X, sp.Y)
		inFOI := !gameplay.OutsideFOI(dist, gameplay.FOIRadius)
		log.Printf(
			"spawned monster entity %d typeId=%d name=%q health=%d moveSpeed=%.2f at (%.1f, %.1f) dist=%.1f foi=%v",
			monster.EntityID(), row.Id, row.Name, row.Health, row.MoveSpeed, sp.X, sp.Y, dist, inFOI,
		)
	}

	return nil
}
