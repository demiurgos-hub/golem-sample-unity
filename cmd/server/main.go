package main

import (
	"context"
	"errors"
	"fmt"
	"log"
	"os"
	"os/signal"
	"sort"
	"syscall"

	"github.com/demiurgos-hub/golem-engine/golem"
	"golem-sample-unity/internal/synced"
)

const moveSpeed = 0.12

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	srv, rt := synced.NewServer(golem.ServerConfig{
		TickRate:          20,
		Addr:              ":8080",
		Transport:         golem.TransportWebTransport,
		StateUpdateLane:   golem.StateUpdateLaneDatagram,
		DevSelfSignedCert: true,
	})

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
		log.Printf("session %d connected, player entity %d spawned", sess.ID, player.EntityID())
	})

	srv.OnDisconnect(func(sess *golem.Session) {
		playerID, ok := playerBySession[sess.ID]
		if !ok {
			return
		}
		delete(playerBySession, sess.ID)
		srv.DeleteEntity(playerID)
		log.Printf("session %d disconnected, player entity %d removed", sess.ID, playerID)
	})

	rt.Commands.OnMove(func(senderID int64, player *synced.SyncedPlayer, cmd *synced.MoveCommand) {
		x, y := player.Position()
		player.SetPosition(x+cmd.Dx*moveSpeed, y+cmd.Dy*moveSpeed)
	})

	rt.Commands.BindAllHandlers(func(sess *golem.Session, err error) {
		log.Printf("command error from session %d: %v", sess.ID, err)
	})

	log.Print("golem sample server listening on https://localhost:8080/wt")
	if err := srv.Run(ctx); err != nil && !errors.Is(err, context.Canceled) {
		log.Fatal(err)
	}
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

	// Fixed, spread-out spawn positions (index-aligned with sorted catalog ids).
	spawnPositions := [][2]float32{
		{5, 0},
		{-5, 2},
	}

	for i, id := range ids {
		row, ok := catalog.Items[id]
		if !ok {
			return fmt.Errorf("MonsterType catalog missing row id %d", id)
		}
		if row.Id <= 0 {
			return fmt.Errorf("MonsterType catalog row has invalid id %d (name=%q); id must be > 0", row.Id, row.Name)
		}

		pos := [2]float32{float32(8 + i*4), float32(i) * 2}
		if i < len(spawnPositions) {
			pos = spawnPositions[i]
		}

		monster := synced.NewSyncedMonster(pos[0], pos[1], uint32(row.Id), row.Health)
		if err := srv.CreateEntity(monster); err != nil {
			return fmt.Errorf("spawn monster type %d (%s): %w", row.Id, row.Name, err)
		}
		log.Printf(
			"spawned monster entity %d typeId=%d name=%q health=%d (from catalog) at (%.1f, %.1f)",
			monster.EntityID(), row.Id, row.Name, row.Health, pos[0], pos[1],
		)
	}

	return nil
}
