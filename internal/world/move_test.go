package world

import "testing"

func TestResolveSlide_FreeMove(t *testing.T) {
	overlaps := func(x, y, r float64) bool { return false }
	nx, ny := ResolveSlide(0, 0, 1, 0.5, ActorRadius, overlaps)
	if nx != 1 || ny != 0.5 {
		t.Fatalf("got (%v,%v)", nx, ny)
	}
}

func TestResolveSlide_FullBlock(t *testing.T) {
	overlaps := func(x, y, r float64) bool { return true }
	nx, ny := ResolveSlide(0, 0, 1, 1, ActorRadius, overlaps)
	if nx != 0 || ny != 0 {
		t.Fatalf("stuck want (0,0), got (%v,%v)", nx, ny)
	}
}

func TestResolveSlide_SlideAlongWall(t *testing.T) {
	// Vertical wall: any x >= 1 overlaps; Y motion stays free.
	overlaps := func(x, y, r float64) bool { return x >= 1 }
	nx, ny := ResolveSlide(0.5, 0, 0.6, 0.4, ActorRadius, overlaps)
	if nx != 0.5 {
		t.Fatalf("X should stay put against wall, got %v", nx)
	}
	if ny != 0.4 {
		t.Fatalf("Y should slide, got %v", ny)
	}
}

func TestResolveSlide_SlideAlongHorizontalWall(t *testing.T) {
	overlaps := func(x, y, r float64) bool { return y >= 1 }
	nx, ny := ResolveSlide(0, 0.5, 0.4, 0.6, ActorRadius, overlaps)
	if ny != 0.5 {
		t.Fatalf("Y should stay put, got %v", ny)
	}
	if nx != 0.4 {
		t.Fatalf("X should slide, got %v", nx)
	}
}
