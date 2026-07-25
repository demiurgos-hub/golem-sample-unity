package gameplay

import (
	"math"
	"sync"
	"time"
)

// PlayerMoveSpeed is server-authoritative player speed in world units per second.
const PlayerMoveSpeed = 4.0

// IntentExpiry is how long a recorded move intent remains valid without refresh.
const IntentExpiry = 250 * time.Millisecond

// Intent is the latest normalized movement direction for one player entity.
type Intent struct {
	DX float64
	DY float64
	At time.Time
}

// IntentStore holds the latest move intent per player entity.
// OnMove handlers and the tick integrator may run concurrently; all access is mutex-guarded.
type IntentStore struct {
	mu       sync.Mutex
	byEntity map[int64]Intent
}

// NewIntentStore creates an empty intent store.
func NewIntentStore() *IntentStore {
	return &IntentStore{byEntity: make(map[int64]Intent)}
}

// NormalizeDirection clamps hostile or malformed client directions.
// NaN/Inf become a zero vector; magnitude above 1 is unit-normalized; magnitude in (0, 1] is kept.
func NormalizeDirection(dx, dy float64) (float64, float64) {
	if math.IsNaN(dx) || math.IsNaN(dy) || math.IsInf(dx, 0) || math.IsInf(dy, 0) {
		return 0, 0
	}
	mag := math.Hypot(dx, dy)
	if mag == 0 {
		return 0, 0
	}
	if mag > 1 {
		return dx / mag, dy / mag
	}
	return dx, dy
}

// Record stores the latest normalized direction for entityID at at.
func (s *IntentStore) Record(entityID int64, dx, dy float64, at time.Time) {
	nx, ny := NormalizeDirection(dx, dy)
	s.mu.Lock()
	s.byEntity[entityID] = Intent{DX: nx, DY: ny, At: at}
	s.mu.Unlock()
}

// Clear removes any stored intent for entityID.
func (s *IntentStore) Clear(entityID int64) {
	s.mu.Lock()
	delete(s.byEntity, entityID)
	s.mu.Unlock()
}

// Active returns a snapshot of non-expired intents at now.
// Expired entries are removed from the store while the lock is held.
func (s *IntentStore) Active(now time.Time, maxAge time.Duration) map[int64]Intent {
	s.mu.Lock()
	defer s.mu.Unlock()

	out := make(map[int64]Intent, len(s.byEntity))
	for id, intent := range s.byEntity {
		if now.Sub(intent.At) > maxAge {
			delete(s.byEntity, id)
			continue
		}
		out[id] = intent
	}
	return out
}
