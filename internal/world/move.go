package world

// ActorRadius is the overlap-query radius used for players and monsters when
// testing candidate positions against placed decor footprints.
const ActorRadius = 0.35

// OverlapFunc reports whether a circle at (x, y) with the given radius overlaps
// blocking decor. Implementations typically wrap Server.OverlapCircle with the
// DecorCollision layer bit.
type OverlapFunc func(x, y, radius float64) bool

// ResolveSlide tries to apply displacement (dx, dy) from (x, y). If the full
// move overlaps decor, it falls back to X-only then Y-only so diagonal motion
// slides along walls instead of sticking.
func ResolveSlide(x, y, dx, dy, radius float64, overlaps OverlapFunc) (nx, ny float64) {
	if overlaps == nil {
		return x + dx, y + dy
	}
	tx, ty := x+dx, y+dy
	if !overlaps(tx, ty, radius) {
		return tx, ty
	}
	nx, ny = x, y
	if !overlaps(x+dx, y, radius) {
		nx = x + dx
	}
	if !overlaps(nx, y+dy, radius) {
		ny = y + dy
	}
	return nx, ny
}
