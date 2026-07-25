package gameplay

import (
	"math"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func TestNormalizeDirection(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name   string
		dx, dy float64
		wantX  float64
		wantY  float64
	}{
		{name: "zero", dx: 0, dy: 0, wantX: 0, wantY: 0},
		{name: "unit", dx: 1, dy: 0, wantX: 1, wantY: 0},
		{name: "partial", dx: 0.5, dy: 0, wantX: 0.5, wantY: 0},
		{name: "too long", dx: 3, dy: 4, wantX: 0.6, wantY: 0.8},
		{name: "nan x", dx: math.NaN(), dy: 1, wantX: 0, wantY: 0},
		{name: "nan y", dx: 1, dy: math.NaN(), wantX: 0, wantY: 0},
		{name: "nan both", dx: math.NaN(), dy: math.NaN(), wantX: 0, wantY: 0},
		{name: "+inf", dx: math.Inf(1), dy: 0, wantX: 0, wantY: 0},
		{name: "-inf", dx: math.Inf(-1), dy: 0, wantX: 0, wantY: 0},
		{name: "inf y", dx: 0, dy: math.Inf(1), wantX: 0, wantY: 0},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			gotX, gotY := NormalizeDirection(tc.dx, tc.dy)
			if math.Abs(gotX-tc.wantX) > 1e-9 || math.Abs(gotY-tc.wantY) > 1e-9 {
				t.Fatalf("NormalizeDirection(%v, %v) = (%v, %v), want (%v, %v)",
					tc.dx, tc.dy, gotX, gotY, tc.wantX, tc.wantY)
			}
		})
	}
}

func TestIntentStoreRecordAndExpire(t *testing.T) {
	t.Parallel()

	store := NewIntentStore()
	now := time.Unix(1000, 0)

	store.Record(1, 1, 0, now)
	store.Record(2, 0, 1, now.Add(-IntentExpiry-time.Millisecond))

	active := store.Active(now, IntentExpiry)
	if len(active) != 1 {
		t.Fatalf("active len = %d, want 1", len(active))
	}
	intent, ok := active[1]
	if !ok {
		t.Fatal("missing entity 1")
	}
	if intent.DX != 1 || intent.DY != 0 {
		t.Fatalf("intent = %+v, want dx=1 dy=0", intent)
	}
	if _, ok := active[2]; ok {
		t.Fatal("expired entity 2 should not be active")
	}

	store.Clear(1)
	if got := store.Active(now, IntentExpiry); len(got) != 0 {
		t.Fatalf("after clear, active = %v, want empty", got)
	}
}

func TestIntentStoreConcurrentRecordAndActive(t *testing.T) {
	store := NewIntentStore()
	const (
		writers = 8
		iters   = 2000
	)

	// Closed set of directions each writer may store for its entity.
	// Alternating two distinct values makes a torn/half-updated read fail the check.
	type dir struct{ dx, dy float64 }
	allowed := make([][2]dir, writers)
	for g := 0; g < writers; g++ {
		allowed[g] = [2]dir{
			{dx: 1, dy: 0},
			{dx: 0, dy: 1},
		}
		if g%2 == 1 {
			allowed[g] = [2]dir{
				{dx: -1, dy: 0},
				{dx: 0, dy: -1},
			}
		}
	}

	var (
		wg        sync.WaitGroup
		badID     atomic.Int64
		badIntent atomic.Int64
		sampleID  atomic.Int64
		sampleDX  atomic.Uint64
		sampleDY  atomic.Uint64
	)
	wg.Add(writers * 2)

	for g := 0; g < writers; g++ {
		entityID := int64(g + 1)
		choices := allowed[g]
		go func(id int64, choices [2]dir) {
			defer wg.Done()
			for i := 0; i < iters; i++ {
				c := choices[i%2]
				store.Record(id, c.dx, c.dy, time.Now())
			}
		}(entityID, choices)

		go func() {
			defer wg.Done()
			for i := 0; i < iters; i++ {
				active := store.Active(time.Now(), IntentExpiry)
				for id, intent := range active {
					if id < 1 || id > int64(writers) {
						badID.Add(1)
						sampleID.Store(id)
						continue
					}
					choices := allowed[id-1]
					ok := (intent.DX == choices[0].dx && intent.DY == choices[0].dy) ||
						(intent.DX == choices[1].dx && intent.DY == choices[1].dy)
					if !ok {
						badIntent.Add(1)
						sampleID.Store(id)
						sampleDX.Store(math.Float64bits(intent.DX))
						sampleDY.Store(math.Float64bits(intent.DY))
					}
				}
			}
		}()
	}

	wg.Wait()
	if n := badID.Load(); n > 0 {
		t.Fatalf("concurrent Active returned %d unexpected entity ids (sample id=%d)", n, sampleID.Load())
	}
	if n := badIntent.Load(); n > 0 {
		id := sampleID.Load()
		t.Fatalf("concurrent Active returned %d intents outside the written set (sample id=%d dx=%v dy=%v)",
			n, id, math.Float64frombits(sampleDX.Load()), math.Float64frombits(sampleDY.Load()))
	}
}
