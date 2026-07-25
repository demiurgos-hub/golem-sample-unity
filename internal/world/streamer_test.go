package world

import (
	"testing"

	"github.com/demiurgos-hub/golem-engine/golem/footprint"
	collisionresolv "github.com/demiurgos-hub/golem-engine/golem/collision/resolv"
	"golem-sample-unity/internal/synced"
)

func loadCatalogAndFootprints(t *testing.T) (*synced.DecorTypeData, *footprint.Set) {
	t.Helper()
	catalog, err := synced.LoadDecorTypeData(DefaultDecorTypeCatalogPath())
	if err != nil {
		t.Fatalf("LoadDecorTypeData: %v", err)
	}
	set, err := footprint.Load(DefaultFootprintsPath())
	if err != nil {
		t.Fatalf("footprint.Load: %v", err)
	}
	return catalog, set
}

func TestUnityGoContract_WallGUIDAndBlockingCount(t *testing.T) {
	catalog, set := loadCatalogAndFootprints(t)

	wall, ok := catalog.Items[1]
	if !ok || !wall.Blocking {
		t.Fatalf("DecorType 1 must be blocking wall, got %+v", wall)
	}
	fp, ok := set.LookupGUID(wall.Prefab)
	if !ok {
		t.Fatalf("wall prefab GUID %q did not resolve", wall.Prefab)
	}
	if wall.Prefab != "63ad6a0a781c6d44ead279f18d921318" {
		t.Fatalf("unexpected wall GUID %q", wall.Prefab)
	}
	if len(fp.Shapes) != 1 || fp.Shapes[0].Layer != LayerDecorCollision {
		t.Fatalf("wall shapes=%+v", fp.Shapes)
	}

	pavement, ok := catalog.Items[2]
	if !ok || pavement.Blocking {
		t.Fatalf("DecorType 2 must be non-blocking pavement, got %+v", pavement)
	}
	pfp, ok := set.LookupGUID(pavement.Prefab)
	if !ok {
		t.Fatalf("pavement GUID missing")
	}
	if len(pfp.Shapes) != 0 {
		t.Fatalf("pavement must have empty shapes, got %d", len(pfp.Shapes))
	}

	db, err := OpenDB(DefaultWorldDBPath())
	if err != nil {
		t.Fatalf("OpenDB: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })
	rows, err := db.QueryAll()
	if err != nil {
		t.Fatalf("QueryAll: %v", err)
	}
	blocking := FilterBlockingInRadius(rows, catalog, set, 0, 0, 1000)
	if len(blocking) != 9 {
		t.Fatalf("blocking rows=%d, want 9", len(blocking))
	}
}

func newTestStreamer(t *testing.T, radius float64) (*Streamer, *DB, *synced.DecorTypeData, *footprint.Set) {
	t.Helper()
	db, err := OpenDB(DefaultWorldDBPath())
	if err != nil {
		t.Fatalf("OpenDB: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })
	catalog, set := loadCatalogAndFootprints(t)
	backend := collisionresolv.New()
	layers := SetupLayers(backend)
	placer := &footprint.Placer2D{
		Backend: backend,
		Resolve: footprint.LayersResolver(layers),
	}
	return NewStreamer(db, set, catalog, placer, radius), db, catalog, set
}

func TestStreamer_PlaceRemoveBalance(t *testing.T) {
	s, _, _, _ := newTestStreamer(t, 8)

	if err := s.Tick([]Anchor{{X: 0, Y: 0}}); err != nil {
		t.Fatalf("Tick in: %v", err)
	}
	st := s.Stats()
	if st.PlaceCalls == 0 || st.Resident != st.PlaceCalls {
		t.Fatalf("after enter: %+v", st)
	}
	placedOnce := st.PlaceCalls

	if err := s.Tick([]Anchor{{X: 1000, Y: 1000}}); err != nil {
		t.Fatalf("Tick out: %v", err)
	}
	st = s.Stats()
	if st.Resident != 0 {
		t.Fatalf("expected empty after leave, got %+v", st)
	}
	if st.RemoveCalls != placedOnce {
		t.Fatalf("remove=%d place=%d", st.RemoveCalls, placedOnce)
	}

	// Re-enter: place again without double-remove on prior handles.
	if err := s.Tick([]Anchor{{X: 0, Y: 0}}); err != nil {
		t.Fatalf("Tick re-enter: %v", err)
	}
	st = s.Stats()
	if st.PlaceCalls != placedOnce*2 {
		t.Fatalf("re-enter places: %+v want PlaceCalls=%d", st, placedOnce*2)
	}
	if st.RemoveCalls != placedOnce {
		t.Fatalf("removes should stay %d until next leave, got %d", placedOnce, st.RemoveCalls)
	}
	if st.Resident != placedOnce {
		t.Fatalf("resident=%d want %d", st.Resident, placedOnce)
	}

	if err := s.Tick([]Anchor{{X: 1000, Y: 1000}}); err != nil {
		t.Fatalf("Tick leave again: %v", err)
	}
	st = s.Stats()
	if st.Resident != 0 || st.PlaceCalls != st.RemoveCalls {
		t.Fatalf("unbalanced after round-trip: %+v", st)
	}
}

func TestStreamer_TwoAnchorsDedup(t *testing.T) {
	const radius = 8.0
	s, db, catalog, set := newTestStreamer(t, radius)
	rows, err := db.QueryAll()
	if err != nil {
		t.Fatalf("QueryAll: %v", err)
	}

	a1 := Anchor{X: 0, Y: 0}
	a2 := Anchor{X: 1, Y: 1}
	want := map[int64]struct{}{}
	for _, r := range FilterBlockingInRadius(rows, catalog, set, a1.X, a1.Y, radius) {
		want[r.ObjectID] = struct{}{}
	}
	for _, r := range FilterBlockingInRadius(rows, catalog, set, a2.X, a2.Y, radius) {
		want[r.ObjectID] = struct{}{}
	}
	if len(want) == 0 {
		t.Fatal("expected overlapping anchors to cover at least one blocking row")
	}

	if err := s.Tick([]Anchor{a1, a2}); err != nil {
		t.Fatalf("Tick: %v", err)
	}
	st := s.Stats()
	if st.PlaceCalls != len(want) {
		t.Fatalf("PlaceCalls=%d want unique rows=%d (double-place?)", st.PlaceCalls, len(want))
	}
	if st.Resident != len(want) {
		t.Fatalf("Resident=%d want %d", st.Resident, len(want))
	}
	if st.RemoveCalls != 0 {
		t.Fatalf("RemoveCalls=%d want 0", st.RemoveCalls)
	}

	// Same two anchors again: must not place a second handle for any shared row.
	if err := s.Tick([]Anchor{a1, a2}); err != nil {
		t.Fatalf("Tick again: %v", err)
	}
	st2 := s.Stats()
	if st2.PlaceCalls != st.PlaceCalls || st2.RemoveCalls != 0 || st2.Resident != len(want) {
		t.Fatalf("dedup broken on re-tick: first=%+v second=%+v wantUnique=%d", st, st2, len(want))
	}
}

func TestStreamer_HysteresisNoBoundaryThrash(t *testing.T) {
	const placeR = 5.0
	s, db, _, _ := newTestStreamer(t, placeR)
	rows, err := db.QueryAll()
	if err != nil {
		t.Fatalf("QueryAll: %v", err)
	}
	var wall DecorRow
	found := false
	for _, r := range rows {
		if r.DecorTypeId == 1 {
			wall = r
			found = true
			break
		}
	}
	if !found {
		t.Fatal("no wall row")
	}

	if err := s.Tick([]Anchor{{X: wall.PosX, Y: wall.PosY}}); err != nil {
		t.Fatalf("Tick place: %v", err)
	}
	if s.Stats().Resident == 0 {
		t.Fatal("expected residents after placing on wall")
	}

	// Settle just outside the place radius (inside remove radius).
	outer := placeR + 0.05
	if outer >= s.RemoveRadius() {
		t.Fatalf("test band %v must be < remove radius %v", outer, s.RemoveRadius())
	}
	if err := s.Tick([]Anchor{{X: wall.PosX, Y: wall.PosY + outer}}); err != nil {
		t.Fatalf("Tick settle: %v", err)
	}
	settled := s.Stats()

	// Oscillate across the place boundary; hysteresis must prevent thrash.
	for i := 0; i < 20; i++ {
		d := placeR - 0.05
		if i%2 == 1 {
			d = placeR + 0.05
		}
		if err := s.Tick([]Anchor{{X: wall.PosX, Y: wall.PosY + d}}); err != nil {
			t.Fatalf("Tick oscillate %d: %v", i, err)
		}
	}
	after := s.Stats()
	if after.PlaceCalls != settled.PlaceCalls || after.RemoveCalls != settled.RemoveCalls {
		t.Fatalf("boundary thrash: settled=%+v afterOscillate=%+v", settled, after)
	}
	if after.Resident == 0 {
		t.Fatal("hysteresis band should keep at least one resident")
	}

	// Far beyond the authored enclosure (and remove radius): everything drops.
	if err := s.Tick([]Anchor{{X: 1000, Y: 1000}}); err != nil {
		t.Fatalf("Tick leave: %v", err)
	}
	left := s.Stats()
	if left.Resident != 0 || left.PlaceCalls != left.RemoveCalls {
		t.Fatalf("leave unbalanced: %+v", left)
	}
}

func TestBlockingProbe_ClampAndSlide(t *testing.T) {
	db, err := OpenDB(DefaultWorldDBPath())
	if err != nil {
		t.Fatalf("OpenDB: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })
	catalog, set := loadCatalogAndFootprints(t)

	backend := collisionresolv.New()
	layers := SetupLayers(backend)
	placer := &footprint.Placer2D{
		Backend: backend,
		Resolve: footprint.LayersResolver(layers),
	}
	s := NewStreamer(db, set, catalog, placer, StreamRadius)
	if err := s.Tick([]PlayerPos{{X: 0, Y: 0}}); err != nil {
		t.Fatalf("Tick: %v", err)
	}
	st := s.Stats()
	t.Logf("placed=%d resident=%d", st.PlaceCalls, st.Resident)
	if st.Resident < 1 {
		t.Fatal("expected walls placed near origin")
	}

	decorMask := layers.Layer(LayerDecorCollision)
	overlaps := func(x, y, radius float64) bool {
		return len(backend.OverlapCircle(x, y, radius, decorMask)) > 0
	}

	// Find a wall and approach from inside the enclosure (south opening is open).
	rows, err := db.QueryAll()
	if err != nil {
		t.Fatal(err)
	}
	var wall DecorRow
	found := false
	for _, r := range rows {
		if r.DecorTypeId == 1 {
			wall = r
			found = true
			break
		}
	}
	if !found {
		t.Fatal("no wall row")
	}

	// Start south of a northern-ish wall center: walk +Y into it.
	startX, startY := wall.PosX, wall.PosY-1.2
	if overlaps(startX, startY, ActorRadius) {
		startX, startY = wall.PosX, wall.PosY+1.2
	}
	if overlaps(startX, startY, ActorRadius) {
		t.Fatalf("could not find clear approach to wall at (%v,%v)", wall.PosX, wall.PosY)
	}

	x, y := startX, startY
	towardY := wall.PosY - y
	// Fixed-tick steps (dt=0.05, speed=4) into the wall.
	step := 4.0 * 0.05
	for i := 0; i < 40; i++ {
		dy := step
		if towardY < 0 {
			dy = -step
		}
		x, y = ResolveSlide(x, y, 0, dy, ActorRadius, overlaps)
	}
	if !overlaps(wall.PosX, wall.PosY, 0.1) {
		t.Fatal("wall footprint missing at wall center")
	}
	if overlaps(x, y, ActorRadius) {
		t.Fatalf("player penetrated wall: pos=(%v,%v) wall=(%v,%v)", x, y, wall.PosX, wall.PosY)
	}
	// Next pure-Y step into the wall must be rejected (clamped).
	nyTry := y + step
	if towardY < 0 {
		nyTry = y - step
	}
	if !overlaps(x, nyTry, ActorRadius) {
		t.Fatalf("expected next Y step into wall to overlap at (%v,%v)", x, nyTry)
	}
	clampedX, clampedY := x, y

	// From the clamped contact pose, diagonal intent must slide on X while Y stays put.
	slideX, slideY := ResolveSlide(clampedX, clampedY, step, step, ActorRadius, overlaps)
	if towardY < 0 {
		slideX, slideY = ResolveSlide(clampedX, clampedY, step, -step, ActorRadius, overlaps)
	}
	if slideY != clampedY {
		t.Fatalf("expected Y held while sliding, got y=%v want %v", slideY, clampedY)
	}
	if slideX == clampedX {
		t.Fatal("expected X slide along wall")
	}
	if overlaps(slideX, slideY, ActorRadius) {
		t.Fatalf("slide ended overlapping at (%v,%v)", slideX, slideY)
	}
	t.Logf("clamp=(%v,%v) slide=(%v,%v) place=%d remove=0 resident=%d",
		clampedX, clampedY, slideX, slideY, st.PlaceCalls, st.Resident)

	if err := s.Tick([]PlayerPos{{X: 500, Y: 500}}); err != nil {
		t.Fatal(err)
	}
	st2 := s.Stats()
	if st2.Resident != 0 || st2.PlaceCalls != st2.RemoveCalls {
		t.Fatalf("leave unbalanced: %+v", st2)
	}
	t.Logf("after leave: place=%d remove=%d resident=%d", st2.PlaceCalls, st2.RemoveCalls, st2.Resident)
}
