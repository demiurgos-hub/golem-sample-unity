package world

import (
	"fmt"
	"math"

	"github.com/demiurgos-hub/golem-engine/golem/collision"
	"github.com/demiurgos-hub/golem-engine/golem/footprint"
	"golem-sample-unity/internal/synced"
)

// Layer names are the Unity↔Go contract. DecorCollision must match footprints.golem.yaml.
const (
	LayerDefault        = "default"
	LayerDecorCollision = "DecorCollision"
)

// StreamRadius is how far around each stream anchor decor collision is kept resident.
// Slightly larger than FOI so walls near the interest edge are already placed.
const StreamRadius = 20.0

// StreamRemoveMargin is added to StreamRadius (or a custom place radius) when
// deciding whether to drop a resident handle. Prevents place/remove thrash when
// an anchor sits exactly on the placement boundary.
const StreamRemoveMargin = 2.0

// Anchor is a streamer interest center (player or monster position).
type Anchor struct {
	X, Y float64
}

// PlayerPos is retained as an alias for Anchor for call-site clarity in probes.
type PlayerPos = Anchor

// StreamStats counts place/remove operations for balance checks.
type StreamStats struct {
	PlaceCalls  int
	RemoveCalls int
	Resident    int
}

// Streamer loads blocking decor near stream anchors into a collision backend each tick.
type Streamer struct {
	DB         *DB
	Footprints *footprint.Set
	Catalog    *synced.DecorTypeData
	Placer     *footprint.Placer2D
	Radius     float64 // place radius; remove uses Radius+StreamRemoveMargin

	placed map[int64]*footprint.Handle2D
	stats  StreamStats
}

// NewStreamer builds a streamer. radius <= 0 uses StreamRadius.
func NewStreamer(db *DB, set *footprint.Set, catalog *synced.DecorTypeData, placer *footprint.Placer2D, radius float64) *Streamer {
	if radius <= 0 {
		radius = StreamRadius
	}
	return &Streamer{
		DB:         db,
		Footprints: set,
		Catalog:    catalog,
		Placer:     placer,
		Radius:     radius,
		placed:     make(map[int64]*footprint.Handle2D),
	}
}

// RemoveRadius is the hysteresis outer radius used when dropping residents.
func (s *Streamer) RemoveRadius() float64 {
	if s == nil {
		return StreamRadius + StreamRemoveMargin
	}
	return s.Radius + StreamRemoveMargin
}

// Stats returns cumulative place/remove counts and current resident handles.
func (s *Streamer) Stats() StreamStats {
	if s == nil {
		return StreamStats{}
	}
	st := s.stats
	st.Resident = len(s.placed)
	return st
}

// Tick refreshes placed footprints for the union of anchor radii.
// Intended to run on the server tick goroutine; the bounds query is cheap for
// the sample database (~14 rows) and does not leave the tick goroutine.
//
// Placement uses Radius; removal uses RemoveRadius() so an anchor oscillating at
// the place boundary does not thrash handles or burn synthetic collision IDs.
func (s *Streamer) Tick(anchors []Anchor) error {
	if s == nil {
		return fmt.Errorf("world streamer is nil")
	}
	toPlace, err := s.blockingNear(anchors, s.Radius)
	if err != nil {
		return err
	}
	toKeep, err := s.blockingNear(anchors, s.RemoveRadius())
	if err != nil {
		return err
	}

	for id, handle := range s.placed {
		if _, ok := toKeep[id]; ok {
			continue
		}
		handle.Remove()
		delete(s.placed, id)
		s.stats.RemoveCalls++
	}

	for id, row := range toPlace {
		if _, ok := s.placed[id]; ok {
			continue
		}
		handle, err := s.placeRow(row)
		if err != nil {
			return err
		}
		if handle == nil {
			continue
		}
		s.placed[id] = handle
		s.stats.PlaceCalls++
	}
	return nil
}

// blockingNear returns ObjectID → row for blocking decor inside radius of any anchor.
func (s *Streamer) blockingNear(anchors []Anchor, radius float64) (map[int64]DecorRow, error) {
	out := make(map[int64]DecorRow)
	if len(anchors) == 0 || radius < 0 {
		return out, nil
	}
	for _, a := range anchors {
		minX, minY := a.X-radius, a.Y-radius
		maxX, maxY := a.X+radius, a.Y+radius
		rows, err := s.DB.QueryBounds(minX, minY, maxX, maxY)
		if err != nil {
			return nil, err
		}
		for _, row := range rows {
			if !s.isBlocking(row) {
				continue
			}
			if math.Hypot(row.PosX-a.X, row.PosY-a.Y) > radius {
				continue
			}
			out[row.ObjectID] = row
		}
	}
	return out, nil
}

func (s *Streamer) isBlocking(row DecorRow) bool {
	if s.Catalog == nil {
		return false
	}
	dt, ok := s.Catalog.Items[row.DecorTypeId]
	if !ok || !dt.Blocking {
		return false
	}
	fp, ok := s.Footprints.LookupGUID(dt.Prefab)
	if !ok || len(fp.Shapes) == 0 {
		return false
	}
	return true
}

func (s *Streamer) placeRow(row DecorRow) (*footprint.Handle2D, error) {
	dt := s.Catalog.Items[row.DecorTypeId]
	fp, ok := s.Footprints.LookupGUID(dt.Prefab)
	if !ok {
		return nil, fmt.Errorf("decor object %d: unknown prefab GUID %q", row.ObjectID, dt.Prefab)
	}
	if len(fp.Shapes) == 0 {
		return nil, nil
	}
	handle, err := s.Placer.Place(fp, footprint.Transform2D{
		X:               row.PosX,
		Y:               row.PosY,
		Scale:           row.Scale,
		RotationDegrees: row.RotZ,
	})
	if err != nil {
		return nil, fmt.Errorf("place decor object %d: %w", row.ObjectID, err)
	}
	return handle, nil
}

// FilterBlockingInRadius is pure helper logic for tests: which rows near (cx,cy)
// are blocking given catalog + footprints.
func FilterBlockingInRadius(rows []DecorRow, catalog *synced.DecorTypeData, set *footprint.Set, cx, cy, radius float64) []DecorRow {
	var out []DecorRow
	for _, row := range rows {
		if catalog == nil {
			continue
		}
		dt, ok := catalog.Items[row.DecorTypeId]
		if !ok || !dt.Blocking {
			continue
		}
		fp, ok := set.LookupGUID(dt.Prefab)
		if !ok || len(fp.Shapes) == 0 {
			continue
		}
		if math.Hypot(row.PosX-cx, row.PosY-cy) > radius {
			continue
		}
		out = append(out, row)
	}
	return out
}

// SetupLayers defines default + DecorCollision and records that they collide.
func SetupLayers(backend collision.Backend) *collision.Layers {
	return collision.NewLayers().
		Bind(backend).
		Define(LayerDefault, LayerDecorCollision).
		SetCollides(LayerDefault, LayerDecorCollision)
}
