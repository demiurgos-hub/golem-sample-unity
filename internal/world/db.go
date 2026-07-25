package world

import (
	"database/sql"
	"fmt"
	"path/filepath"
	"strings"

	_ "modernc.org/sqlite"
)

// DecorRow is one WorldDecorations record from the authored world database.
type DecorRow struct {
	ObjectID    int64
	DecorTypeId int32
	PosX        float64
	PosY        float64
	RotZ        float64
	Scale       float64
}

// DB is a read-only handle to the world decorations SQLite database.
type DB struct {
	sql *sql.DB
}

// OpenDB opens path read-only using the pure-Go modernc.org/sqlite driver (no cgo).
func OpenDB(path string) (*DB, error) {
	abs, err := filepath.Abs(path)
	if err != nil {
		return nil, fmt.Errorf("world db abs path: %w", err)
	}
	// Read-only URI. Triple-slash form keeps Windows drive letters valid (file:///C:/...).
	slash := filepath.ToSlash(abs)
	if !strings.HasPrefix(slash, "/") {
		slash = "/" + slash
	}
	dsn := "file://" + slash + "?mode=ro&_pragma=query_only(1)"
	db, err := sql.Open("sqlite", dsn)
	if err != nil {
		return nil, fmt.Errorf("open world db %s: %w", abs, err)
	}
	db.SetMaxOpenConns(1)
	if err := db.Ping(); err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("ping world db %s: %w", abs, err)
	}
	return &DB{sql: db}, nil
}

// Close closes the underlying database handle.
func (d *DB) Close() error {
	if d == nil || d.sql == nil {
		return nil
	}
	return d.sql.Close()
}

// QueryBounds returns WorldDecorations rows whose positions fall inside the
// inclusive-exclusive axis-aligned bounds, mirroring Fractured WorldStreamer.QueryObjects
// (PosX/PosY here; Fractured uses PosX/PosZ for the horizontal plane).
func (d *DB) QueryBounds(minX, minY, maxX, maxY float64) ([]DecorRow, error) {
	if d == nil || d.sql == nil {
		return nil, fmt.Errorf("world db is nil")
	}
	const q = `
SELECT ObjectID, DecorTypeId, PosX, PosY, RotZ, Scale
FROM WorldDecorations
WHERE PosX >= ? AND PosY >= ? AND PosX < ? AND PosY < ?`
	rows, err := d.sql.Query(q, minX, minY, maxX, maxY)
	if err != nil {
		return nil, fmt.Errorf("query WorldDecorations bounds: %w", err)
	}
	defer rows.Close()

	var out []DecorRow
	for rows.Next() {
		var r DecorRow
		if err := rows.Scan(&r.ObjectID, &r.DecorTypeId, &r.PosX, &r.PosY, &r.RotZ, &r.Scale); err != nil {
			return nil, fmt.Errorf("scan WorldDecorations: %w", err)
		}
		out = append(out, r)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	return out, nil
}

// QueryAll returns every WorldDecorations row (tests / full-world probes).
func (d *DB) QueryAll() ([]DecorRow, error) {
	if d == nil || d.sql == nil {
		return nil, fmt.Errorf("world db is nil")
	}
	const q = `SELECT ObjectID, DecorTypeId, PosX, PosY, RotZ, Scale FROM WorldDecorations`
	rows, err := d.sql.Query(q)
	if err != nil {
		return nil, fmt.Errorf("query WorldDecorations: %w", err)
	}
	defer rows.Close()

	var out []DecorRow
	for rows.Next() {
		var r DecorRow
		if err := rows.Scan(&r.ObjectID, &r.DecorTypeId, &r.PosX, &r.PosY, &r.RotZ, &r.Scale); err != nil {
			return nil, fmt.Errorf("scan WorldDecorations: %w", err)
		}
		out = append(out, r)
	}
	return out, rows.Err()
}
