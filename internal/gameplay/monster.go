package gameplay

import (
	"math"
	"math/rand"
)

// Interest / FOI tuning for the sample world.
//
// CellSize=8 enables the spatial hash (CellSize > 0). With players moving at
// PlayerMoveSpeed (4 u/s) and an FOI radius of 15, a stationary client sees
// roughly a 30-unit-wide disk; monsters spawned beyond that start outside
// interest so enter/exit churn is observable without walking far.
const (
	InterestCellSize = 8.0
	FOIRadius        = 15.0
	FOIMargin        = 2.0

	// ChaseRadius is how close a player must be for a monster to chase.
	ChaseRadius = 10.0

	// WanderRadius caps how far a monster may drift from its spawn/home.
	// Keeps inside-FOI and outside-FOI populations in their intended bands.
	WanderRadius = 4.0

	WanderHoldMin = 1.0 // seconds
	WanderHoldMax = 3.0 // seconds
)

// MonsterSpawnSpec is one sample spawn: TypeIndex indexes sorted catalog ids.
type MonsterSpawnSpec struct {
	TypeIndex int
	X, Y      float64
}

// SampleMonsterSpawns is the authoritative spawn layout used by the server.
// Distances are relative to the player spawn at the origin and FOIRadius:
// some start inside FOI, some outside so interest enter/exit is exercised.
var SampleMonsterSpawns = []MonsterSpawnSpec{
	// Inside FOI (~dist 5).
	{0, 5, 0},
	{0, -4, 3},
	{1, 3, -4},
	// Outside FOI (dist 30–50).
	{0, 40, 0},
	{0, 0, 40},
	{0, -35, -35},
	{1, -40, 5},
	{1, 30, -30},
}

// OutsideFOI reports whether dist from the FOI anchor exceeds the enter radius.
func OutsideFOI(dist, radius float64) bool {
	return dist > radius
}

// Vec2 is a 2D position or direction.
type Vec2 struct {
	X, Y float64
}

// MonsterView is the per-tick input for one living monster.
type MonsterView struct {
	EntityID int64
	Pos      Vec2
	TypeID   uint32
	Speed    float64 // world units/second from MonsterType.MoveSpeed
}

// PlayerView is the per-tick input for one living player.
type PlayerView struct {
	Pos Vec2
}

// MonsterDisplacement is the integrated position change for one monster this tick.
type MonsterDisplacement struct {
	EntityID int64
	X, Y     float64
}

// WanderState is per-monster wander memory. Touched only from the tick goroutine.
// Home is latched to the first observed position (spawn).
type WanderState struct {
	DirX, DirY float64
	HoldLeft   float64
	HomeX      float64
	HomeY      float64
	HasHome    bool
}

// MonsterAI holds wander state and a seeded RNG for deterministic tests / sample runs.
// Assumption: Step is called only from the engine tick goroutine (OnConnect,
// OnDisconnect, command dispatch, and OnTick all run there via drainMessages).
type MonsterAI struct {
	chaseRadius   float64
	wanderRadius  float64
	wanderHoldMin float64
	wanderHoldMax float64
	rng           *rand.Rand
	states        map[int64]WanderState
}

// NewMonsterAI creates AI state using rng for wander direction/hold picks.
// rng must be non-nil.
func NewMonsterAI(rng *rand.Rand) *MonsterAI {
	if rng == nil {
		panic("gameplay: NewMonsterAI requires a non-nil *rand.Rand")
	}
	return &MonsterAI{
		chaseRadius:   ChaseRadius,
		wanderRadius:  WanderRadius,
		wanderHoldMin: WanderHoldMin,
		wanderHoldMax: WanderHoldMax,
		rng:           rng,
		states:        make(map[int64]WanderState),
	}
}

// Forget drops wander state for a removed monster entity.
func (ai *MonsterAI) Forget(entityID int64) {
	delete(ai.states, entityID)
}

// Step advances wander/chase for the given living monsters and returns new positions.
// Dead entities are not in monsters; their wander state is pruned.
// Speed comes from each MonsterView.Speed (catalog MoveSpeed), not a constant.
func (ai *MonsterAI) Step(dt float64, monsters []MonsterView, players []PlayerView) []MonsterDisplacement {
	if dt <= 0 || len(monsters) == 0 {
		ai.prune(nil)
		return nil
	}

	alive := make(map[int64]struct{}, len(monsters))
	out := make([]MonsterDisplacement, 0, len(monsters))

	for _, m := range monsters {
		alive[m.EntityID] = struct{}{}
		if m.Speed <= 0 {
			continue
		}

		st := ai.states[m.EntityID]
		if !st.HasHome {
			st.HomeX, st.HomeY = m.Pos.X, m.Pos.Y
			st.HasHome = true
		}
		home := Vec2{X: st.HomeX, Y: st.HomeY}

		dx, dy, st := DecideMove(m.Pos, home, ai.wanderRadius, st, players, ai.chaseRadius, dt, ai.wanderHoldMin, ai.wanderHoldMax, ai.rng)
		ai.states[m.EntityID] = st

		out = append(out, MonsterDisplacement{
			EntityID: m.EntityID,
			X:        m.Pos.X + dx*m.Speed*dt,
			Y:        m.Pos.Y + dy*m.Speed*dt,
		})
	}

	ai.prune(alive)
	return out
}

func (ai *MonsterAI) prune(alive map[int64]struct{}) {
	for id := range ai.states {
		if alive == nil {
			delete(ai.states, id)
			continue
		}
		if _, ok := alive[id]; !ok {
			delete(ai.states, id)
		}
	}
}

// DecideMove chooses a unit direction: chase nearest player in radius, else wander.
// Pure aside from rng; suitable for deterministic unit tests with a seeded Rand.
// At exact player overlap, NearestPlayer returns a zero direction (hold still).
func DecideMove(
	pos, home Vec2,
	wanderRadius float64,
	wander WanderState,
	players []PlayerView,
	chaseRadius float64,
	dt float64,
	holdMin, holdMax float64,
	rng *rand.Rand,
) (dx, dy float64, next WanderState) {
	if px, py, ok := NearestPlayer(pos, players, chaseRadius); ok {
		return px, py, wander
	}
	return AdvanceWander(pos, home, wanderRadius, wander, dt, holdMin, holdMax, rng)
}

// NearestPlayer returns a unit direction toward the closest player within radius.
// When overlapping a player (d2 == 0), it returns (0, 0, true) so the monster holds still.
func NearestPlayer(pos Vec2, players []PlayerView, radius float64) (dx, dy float64, ok bool) {
	if radius <= 0 || len(players) == 0 {
		return 0, 0, false
	}
	r2 := radius * radius
	best := r2
	var bestDX, bestDY float64
	found := false
	for _, p := range players {
		ddx := p.Pos.X - pos.X
		ddy := p.Pos.Y - pos.Y
		d2 := ddx*ddx + ddy*ddy
		if d2 > r2 {
			continue
		}
		if d2 == 0 {
			return 0, 0, true
		}
		if !found || d2 < best {
			best = d2
			mag := math.Sqrt(d2)
			bestDX, bestDY = ddx/mag, ddy/mag
			found = true
		}
	}
	return bestDX, bestDY, found
}

// AdvanceWander holds the current wander direction for HoldLeft seconds, then re-picks.
// Directions are constrained so the monster stays near home within wanderRadius.
func AdvanceWander(
	pos, home Vec2,
	wanderRadius float64,
	wander WanderState,
	dt, holdMin, holdMax float64,
	rng *rand.Rand,
) (dx, dy float64, next WanderState) {
	next = wander
	next.HoldLeft -= dt
	if next.HoldLeft <= 0 || (next.DirX == 0 && next.DirY == 0) {
		next.DirX, next.DirY = PickWanderDirection(rng)
		next.HoldLeft = RandomHold(holdMin, holdMax, rng)
	}
	dx, dy = ConstrainWanderDirection(pos, home, wanderRadius, next.DirX, next.DirY)
	next.DirX, next.DirY = dx, dy
	return dx, dy, next
}

// ConstrainWanderDirection returns dir unchanged while inside the wander disk;
// outside it, returns a unit vector toward home.
func ConstrainWanderDirection(pos, home Vec2, wanderRadius, dirX, dirY float64) (dx, dy float64) {
	if wanderRadius <= 0 {
		return dirX, dirY
	}
	ox := pos.X - home.X
	oy := pos.Y - home.Y
	dist := math.Hypot(ox, oy)
	if dist <= wanderRadius {
		return dirX, dirY
	}
	if dist == 0 {
		return dirX, dirY
	}
	return -ox / dist, -oy / dist
}

// PickWanderDirection returns a unit vector in a random direction.
func PickWanderDirection(rng *rand.Rand) (dx, dy float64) {
	angle := rng.Float64() * 2 * math.Pi
	return math.Cos(angle), math.Sin(angle)
}

// RandomHold returns a hold duration in [holdMin, holdMax].
func RandomHold(holdMin, holdMax float64, rng *rand.Rand) float64 {
	if holdMax <= holdMin {
		return holdMin
	}
	return holdMin + rng.Float64()*(holdMax-holdMin)
}
