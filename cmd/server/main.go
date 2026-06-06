package main

import (
	"context"
	"errors"
	"log"
	"os"
	"os/signal"
	"syscall"

	"golem-engine/golem"
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
		DevSelfSignedCert: true,
	})

	playerBySession := make(map[int64]int64)

	srv.OnConnect(func(sess *golem.Session) {
		player := synced.NewSyncedPlayer(0, 0)
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
