package world

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
)

// RepoRoot returns the sample repository root (directory containing go.mod).
// It walks upward from this source file's compile-time path so the result does
// not depend on the process working directory. Relocated binaries where that
// path no longer exists return an error — callers should require an explicit
// -world-db (and related) flag rather than guessing.
func RepoRoot() (string, error) {
	_, file, _, ok := runtime.Caller(0)
	if !ok {
		return "", fmt.Errorf("could not resolve repository root: runtime.Caller failed; pass -world-db explicitly (e.g. path/to/unity/Assets/StreamingAssets/world.db)")
	}
	dir := filepath.Dir(file)
	for {
		if _, err := os.Stat(filepath.Join(dir, "go.mod")); err == nil {
			return dir, nil
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			return "", fmt.Errorf("could not resolve repository root from compiled source path %q (binary relocated or sources removed); pass -world-db explicitly (e.g. path/to/unity/Assets/StreamingAssets/world.db)", file)
		}
		dir = parent
	}
}

// DefaultWorldDBPath is the committed SQLite world database under StreamingAssets.
// Returns "" when RepoRoot cannot be resolved so flag defaults do not silently
// point at a cwd-relative guess.
func DefaultWorldDBPath() string {
	root, err := RepoRoot()
	if err != nil {
		return ""
	}
	return filepath.Join(root, "unity", "Assets", "StreamingAssets", "world.db")
}

// DefaultFootprintsPath is the Scribe-exported footprint catalog at the repo root.
func DefaultFootprintsPath() string {
	root, err := RepoRoot()
	if err != nil {
		return ""
	}
	return filepath.Join(root, "footprints.golem.yaml")
}

// DefaultDecorTypeCatalogPath is the Scribe-exported DecorType catalog.
func DefaultDecorTypeCatalogPath() string {
	root, err := RepoRoot()
	if err != nil {
		return ""
	}
	return filepath.Join(root, "catalogs", "decor_type.golem.yaml")
}
