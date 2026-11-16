package world

import (
	"math"
	"testing"
)

func TestQueryBounds_FiltersByAABB(t *testing.T) {
	db, err := OpenDB(DefaultWorldDBPath())
	if err != nil {
		t.Fatalf("OpenDB: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })

	all, err := db.QueryAll()
	if err != nil {
		t.Fatalf("QueryAll: %v", err)
	}
	if len(all) != 14 {
		t.Fatalf("QueryAll len=%d, want 14", len(all))
	}

	// Tight box around origin should miss far tiles if any; enclosure sits near origin.
	near, err := db.QueryBounds(-2, -2, 2, 2)
	if err != nil {
		t.Fatalf("QueryBounds: %v", err)
	}
	if len(near) == 0 {
		t.Fatal("expected some decor near origin")
	}
	for _, r := range near {
		if r.PosX < -2 || r.PosY < -2 || r.PosX >= 2 || r.PosY >= 2 {
			t.Fatalf("row %+v outside requested bounds", r)
		}
	}

	far, err := db.QueryBounds(1000, 1000, 1001, 1001)
	if err != nil {
		t.Fatalf("QueryBounds far: %v", err)
	}
	if len(far) != 0 {
		t.Fatalf("far bounds returned %d rows", len(far))
	}
}

func TestFilterBlockingInRadius(t *testing.T) {
	db, err := OpenDB(DefaultWorldDBPath())
	if err != nil {
		t.Fatalf("OpenDB: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })

	rows, err := db.QueryAll()
	if err != nil {
		t.Fatalf("QueryAll: %v", err)
	}

	catalog, _ := loadCatalogAndTemplates(t)

	blocking := FilterBlockingInRadius(rows, catalog, 0, 0, 100)
	if len(blocking) != 9 {
		t.Fatalf("blocking rows=%d, want 9 walls", len(blocking))
	}
	for _, r := range blocking {
		if r.DecorTypeId != 1 {
			t.Fatalf("unexpected decor type %d in blocking set", r.DecorTypeId)
		}
	}

	pavementOnly := 0
	for _, r := range rows {
		if r.DecorTypeId == 2 {
			pavementOnly++
		}
	}
	if pavementOnly != 5 {
		t.Fatalf("pavement rows=%d, want 5", pavementOnly)
	}

	none := FilterBlockingInRadius(rows, catalog, 0, 0, 0.01)
	// Walls sit on integer coords around an enclosure; origin may or may not host one.
	for _, r := range none {
		if math.Hypot(r.PosX, r.PosY) > 0.01 {
			t.Fatalf("row outside tiny radius: %+v", r)
		}
	}
}
