package gameplay

import (
	"math"
	"math/rand"
	"testing"
)

func TestNearestPlayerChaseVersusWanderSelection(t *testing.T) {
	t.Parallel()

	pos := Vec2{X: 0, Y: 0}
	players := []PlayerView{
		{Pos: Vec2{X: 3, Y: 0}},  // nearer
		{Pos: Vec2{X: 8, Y: 0}},  // farther but still inside
		{Pos: Vec2{X: 20, Y: 0}}, // outside ChaseRadius
	}

	dx, dy, ok := NearestPlayer(pos, players, ChaseRadius)
	if !ok {
		t.Fatal("expected a chase target inside ChaseRadius")
	}
	if math.Abs(dx-1) > 1e-9 || math.Abs(dy) > 1e-9 {
		t.Fatalf("nearest direction = (%v, %v), want (1, 0)", dx, dy)
	}

	_, _, ok = NearestPlayer(pos, []PlayerView{{Pos: Vec2{X: 20, Y: 0}}}, ChaseRadius)
	if ok {
		t.Fatal("player outside ChaseRadius must not be selected")
	}
}

func TestNearestPlayerHoldsStillAtOverlap(t *testing.T) {
	t.Parallel()

	dx, dy, ok := NearestPlayer(Vec2{}, []PlayerView{{Pos: Vec2{}}}, ChaseRadius)
	if !ok {
		t.Fatal("overlap must count as a chase target")
	}
	if dx != 0 || dy != 0 {
		t.Fatalf("overlap direction = (%v, %v), want (0, 0)", dx, dy)
	}

	rng := rand.New(rand.NewSource(1))
	wander := WanderState{DirX: 0, DirY: 1, HoldLeft: 5, HasHome: true}
	gotDX, gotDY, next := DecideMove(Vec2{}, Vec2{}, WanderRadius, wander, []PlayerView{{Pos: Vec2{}}}, ChaseRadius, 0.05, WanderHoldMin, WanderHoldMax, rng)
	if gotDX != 0 || gotDY != 0 {
		t.Fatalf("DecideMove at overlap = (%v, %v), want (0, 0)", gotDX, gotDY)
	}
	if next != wander {
		t.Fatalf("wander state mutated at overlap: got %+v want %+v", next, wander)
	}
}

func TestDecideMovePrefersChaseOverWander(t *testing.T) {
	t.Parallel()

	rng := rand.New(rand.NewSource(1))
	wander := WanderState{DirX: 0, DirY: 1, HoldLeft: 5, HasHome: true}
	players := []PlayerView{{Pos: Vec2{X: 4, Y: 0}}}

	dx, dy, next := DecideMove(Vec2{}, Vec2{}, WanderRadius, wander, players, ChaseRadius, 0.05, WanderHoldMin, WanderHoldMax, rng)
	if math.Abs(dx-1) > 1e-9 || math.Abs(dy) > 1e-9 {
		t.Fatalf("chase dir = (%v, %v), want (1, 0)", dx, dy)
	}
	if next != wander {
		t.Fatalf("wander state mutated during chase: got %+v want %+v", next, wander)
	}
}

func TestAdvanceWanderRepicksAfterHold(t *testing.T) {
	t.Parallel()

	rng := rand.New(rand.NewSource(42))
	home := Vec2{}
	st := WanderState{DirX: 1, DirY: 0, HoldLeft: 0.05, HasHome: true}

	dx, dy, st := AdvanceWander(home, home, WanderRadius, st, 0.05, 1, 1, rng)
	if st.HoldLeft != 1 {
		t.Fatalf("HoldLeft after expiry = %v, want 1", st.HoldLeft)
	}
	if math.Abs(math.Hypot(dx, dy)-1) > 1e-9 {
		t.Fatalf("re-picked direction not unit: (%v,%v)", dx, dy)
	}

	before := st
	dx, dy, st = AdvanceWander(home, home, WanderRadius, st, 0.05, 1, 1, rng)
	if dx != before.DirX || dy != before.DirY {
		t.Fatalf("direction changed during hold: (%v,%v) -> (%v,%v)", before.DirX, before.DirY, dx, dy)
	}
	if math.Abs(st.HoldLeft-0.95) > 1e-9 {
		t.Fatalf("HoldLeft = %v, want 0.95", st.HoldLeft)
	}
}

func TestConstrainWanderDirectionPullsHome(t *testing.T) {
	t.Parallel()

	pos := Vec2{X: 10, Y: 0}
	home := Vec2{}
	dx, dy := ConstrainWanderDirection(pos, home, WanderRadius, 1, 0) // outward
	if math.Abs(dx-(-1)) > 1e-9 || math.Abs(dy) > 1e-9 {
		t.Fatalf("outside constrain = (%v,%v), want (-1,0)", dx, dy)
	}

	inside := Vec2{X: 1, Y: 0}
	dx, dy = ConstrainWanderDirection(inside, home, WanderRadius, 0, 1)
	if dx != 0 || dy != 1 {
		t.Fatalf("inside constrain mutated dir: (%v,%v)", dx, dy)
	}
}

func TestMonsterAIWanderStaysNearHome(t *testing.T) {
	t.Parallel()

	ai := NewMonsterAI(rand.New(rand.NewSource(3)))
	home := Vec2{X: 40, Y: 0}
	monsters := []MonsterView{{EntityID: 1, Pos: home, TypeID: 1, Speed: 2.5}}
	const dt = 0.05
	// 20s of wander with no players.
	for i := 0; i < 400; i++ {
		got := ai.Step(dt, monsters, nil)
		if len(got) != 1 {
			t.Fatalf("tick %d: displacements = %d", i, len(got))
		}
		monsters[0].Pos = Vec2{X: got[0].X, Y: got[0].Y}
		dist := math.Hypot(monsters[0].Pos.X-home.X, monsters[0].Pos.Y-home.Y)
		// One integration step may overshoot the disk by speed*dt.
		if dist > WanderRadius+2.5*dt+1e-6 {
			t.Fatalf("tick %d: dist from home = %v, want <= %v", i, dist, WanderRadius+2.5*dt)
		}
	}
}

func TestMonsterAIStepUsesPerTypeSpeed(t *testing.T) {
	t.Parallel()

	rng := rand.New(rand.NewSource(7))
	ai := NewMonsterAI(rng)

	players := []PlayerView{{Pos: Vec2{X: 5, Y: 0}}}
	monsters := []MonsterView{
		{EntityID: 1, Pos: Vec2{}, TypeID: 1, Speed: 1.5},
		{EntityID: 2, Pos: Vec2{}, TypeID: 2, Speed: 2.5},
	}
	const dt = 0.05
	got := ai.Step(dt, monsters, players)
	if len(got) != 2 {
		t.Fatalf("displacements = %d, want 2", len(got))
	}

	byID := map[int64]MonsterDisplacement{}
	for _, d := range got {
		byID[d.EntityID] = d
	}
	if math.Abs(byID[1].X-1.5*dt) > 1e-9 || math.Abs(byID[1].Y) > 1e-9 {
		t.Fatalf("type1 move = (%v,%v), want (%v,0)", byID[1].X, byID[1].Y, 1.5*dt)
	}
	if math.Abs(byID[2].X-2.5*dt) > 1e-9 || math.Abs(byID[2].Y) > 1e-9 {
		t.Fatalf("type2 move = (%v,%v), want (%v,0)", byID[2].X, byID[2].Y, 2.5*dt)
	}
}

func TestMonsterAIStepPrunesMissingEntities(t *testing.T) {
	t.Parallel()

	ai := NewMonsterAI(rand.New(rand.NewSource(1)))
	_ = ai.Step(0.05, []MonsterView{{EntityID: 9, Pos: Vec2{}, TypeID: 1, Speed: 1}}, nil)
	if _, ok := ai.states[9]; !ok {
		t.Fatal("expected wander state for entity 9")
	}
	_ = ai.Step(0.05, nil, nil)
	if _, ok := ai.states[9]; ok {
		t.Fatal("expected wander state pruned when monster absent")
	}
}

func TestSpawnLayoutExercisesFOI(t *testing.T) {
	t.Parallel()

	if len(SampleMonsterSpawns) == 0 {
		t.Fatal("SampleMonsterSpawns is empty")
	}

	var inside, outside int
	for _, sp := range SampleMonsterSpawns {
		d := math.Hypot(sp.X, sp.Y)
		if OutsideFOI(d, FOIRadius) {
			outside++
		} else {
			inside++
		}
	}
	if inside == 0 {
		t.Fatal("SampleMonsterSpawns has no spawns inside FOI")
	}
	if outside == 0 {
		t.Fatal("SampleMonsterSpawns has no spawns outside FOI")
	}
}

func TestPickWanderDirectionDeterministicWithSeed(t *testing.T) {
	t.Parallel()

	a := rand.New(rand.NewSource(99))
	b := rand.New(rand.NewSource(99))
	ax, ay := PickWanderDirection(a)
	bx, by := PickWanderDirection(b)
	if ax != bx || ay != by {
		t.Fatalf("seeded RNG diverged: (%v,%v) vs (%v,%v)", ax, ay, bx, by)
	}
}
