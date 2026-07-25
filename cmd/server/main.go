package main

import (
	"context"
	"errors"
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
	"golem-sample-unity/internal/gameplay"
	"golem-sample-unity/internal/synced"
)

func main() {
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

	srv.OnTick(func(dt float64, s *golem.Server) {
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
			player.SetPosition(
				x+float32(intent.DX*gameplay.PlayerMoveSpeed*dt),
				y+float32(intent.DY*gameplay.PlayerMoveSpeed*dt),
			)
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
			monster.SetPosition(float32(d.X), float32(d.Y))
		}
	})

	rt.Commands.BindAllHandlers(func(sess *golem.Session, err error) {
		log.Printf("command error from session %d: %v", sess.ID, err)
	})

	log.Printf(
		"golem sample server listening on https://localhost:8080/wt (CellSize=%.0f FOIRadius=%.0f)",
		gameplay.InterestCellSize, gameplay.FOIRadius,
	)
	if err := srv.Run(ctx); err != nil && !errors.Is(err, context.Canceled) {
		log.Fatal(err)
	}
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
