using System.Collections.Generic;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Streams decor visuals from world.db around the local player. Client visual radius is
    /// independent of the server's collision StreamRadius — do not couple them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DecorStreamer : MonoBehaviour
    {
        [SerializeField]
        private DecorList decorList;

        [SerializeField]
        private LocalPlayer localPlayer;

        [SerializeField]
        private Transform runtimeRoot;

        [Tooltip("Client-only visual place radius (world units). Not the server collision radius.")]
        [SerializeField]
        private float streamRadius = 28f;

        [Tooltip("Added to streamRadius when deciding whether to despawn. Mirrors server StreamRemoveMargin.")]
        [SerializeField]
        private float streamRemoveMargin = 2f;

        [Tooltip("Re-query the database only after the anchor moves at least this far.")]
        [SerializeField]
        private float requeryDistance = 4f;

        [SerializeField]
        private int instantiationsPerFrame = 8;

        private readonly Dictionary<long, GameObject> loaded = new();
        private readonly List<WorldDecorRow> pendingSpawn = new();
        private readonly List<long> toRemove = new();
        private readonly HashSet<long> keepIds = new();
        private readonly HashSet<long> rejectedObjectIds = new();

        private WorldDatabase database;
        private Vector2 lastQueryCenter;
        private bool hasQueryCenter;
        private long trackedEntityId;
        private Transform anchor;
        private bool openFailed;

        /// <summary>Number of currently instantiated streamed decor objects.</summary>
        public int LoadedCount => loaded.Count;

        /// <summary>Rows waiting on the per-frame instantiation budget.</summary>
        public int PendingSpawnCount => pendingSpawn.Count;

        /// <summary>Despawn radius = place radius + remove margin (hysteresis band).</summary>
        private float RemoveRadius => streamRadius + Mathf.Max(0f, streamRemoveMargin);

        private void Awake()
        {
            if (runtimeRoot == null)
            {
                runtimeRoot = transform;
            }

            if (localPlayer == null)
            {
                localPlayer = FindFirstObjectByType<LocalPlayer>();
            }
        }

        private void OnEnable()
        {
            if (localPlayer != null)
            {
                localPlayer.Assigned += OnLocalPlayerAssigned;
                if (localPlayer.IsAssigned)
                {
                    OnLocalPlayerAssigned(localPlayer.EntityId);
                }
            }
        }

        private void OnDisable()
        {
            if (localPlayer != null)
            {
                localPlayer.Assigned -= OnLocalPlayerAssigned;
            }
        }

        private void OnDestroy()
        {
            ClearAllInstances();
            database?.Dispose();
            database = null;
        }

        private void Update()
        {
            if (openFailed || decorList == null)
            {
                return;
            }

            if (!EnsureDatabase())
            {
                return;
            }

            if (!TryResolveAnchor())
            {
                return;
            }

            var center = (Vector2)anchor.position;
            var movedFar =
                !hasQueryCenter ||
                (center - lastQueryCenter).sqrMagnitude >= requeryDistance * requeryDistance;

            if (movedFar)
            {
                RefreshQuery(center);
            }

            DrainSpawnBudget();
        }

        private void OnLocalPlayerAssigned(long entityId)
        {
            trackedEntityId = entityId;
            anchor = null;
            hasQueryCenter = false;
            pendingSpawn.Clear();
        }

        private bool EnsureDatabase()
        {
            if (database != null)
            {
                return true;
            }

            try
            {
                database = WorldDatabase.OpenDefault();
                return true;
            }
            catch (System.Exception ex)
            {
                openFailed = true;
                Debug.LogError($"[DecorStreamer] Failed to open world.db: {ex.Message}", this);
                return false;
            }
        }

        private bool TryResolveAnchor()
        {
            if (anchor != null)
            {
                return true;
            }

            if (trackedEntityId == 0 && localPlayer != null && localPlayer.IsAssigned)
            {
                trackedEntityId = localPlayer.EntityId;
            }

            if (trackedEntityId == 0)
            {
                return false;
            }

            if (PlayerVisual.TryGet(trackedEntityId, out var visual) && visual != null)
            {
                anchor = visual.transform;
                return true;
            }

            return false;
        }

        private void RefreshQuery(Vector2 center)
        {
            lastQueryCenter = center;
            hasQueryCenter = true;

            // Place uses streamRadius; keep/remove uses RemoveRadius (same split as Go Streamer.Tick).
            var removeRadius = RemoveRadius;
            var minX = center.x - removeRadius;
            var minY = center.y - removeRadius;
            var maxX = center.x + removeRadius;
            var maxY = center.y + removeRadius;

            List<WorldDecorRow> rows;
            try
            {
                rows = database.QueryBounds(minX, minY, maxX, maxY);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DecorStreamer] QueryBounds failed: {ex.Message}", this);
                return;
            }

            keepIds.Clear();
            var placeSq = streamRadius * streamRadius;
            var removeSq = removeRadius * removeRadius;

            pendingSpawn.Clear();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!IsFootprintCompatible(row, out var rejectReason))
                {
                    RejectRow(row.ObjectId, rejectReason);
                    continue;
                }

                var dx = row.PosX - center.x;
                var dy = row.PosY - center.y;
                var distSq = dx * dx + dy * dy;
                if (distSq > removeSq)
                {
                    continue;
                }

                // Inside remove radius: keep if already loaded (hysteresis band).
                keepIds.Add(row.ObjectId);

                // Only instantiate inside the tighter place radius.
                if (distSq <= placeSq && !loaded.ContainsKey(row.ObjectId))
                {
                    pendingSpawn.Add(row);
                }
            }

            toRemove.Clear();
            foreach (var pair in loaded)
            {
                if (!keepIds.Contains(pair.Key))
                {
                    toRemove.Add(pair.Key);
                }
            }

            for (var i = 0; i < toRemove.Count; i++)
            {
                DestroyLoaded(toRemove[i]);
            }
        }

        /// <summary>
        /// Same acceptance rule as golem/footprint.Placer2D (validateScale + quarterTurnIndex):
        /// Scale &gt; 0 and finite; RotZ normalizes to an exact 0/90/180/270 quarter turn.
        /// Rows that fail would render without server collision — skip them.
        /// </summary>
        private static bool IsFootprintCompatible(WorldDecorRow row, out string reason)
        {
            if (float.IsNaN(row.Scale) || float.IsInfinity(row.Scale) || row.Scale <= 0f)
            {
                reason = $"scale must be > 0 (got {row.Scale})";
                return false;
            }

            if (float.IsNaN(row.RotZ) || float.IsInfinity(row.RotZ))
            {
                reason = $"rotation must be finite (got RotZ={row.RotZ})";
                return false;
            }

            // Mirrors footprint.quarterTurnIndex: math.Mod then lift negatives into [0,360).
            var mod = row.RotZ % 360f;
            if (mod < 0f)
            {
                mod += 360f;
            }

            if (mod != 0f && mod != 90f && mod != 180f && mod != 270f)
            {
                reason =
                    $"rotation must be an exact quarter turn in degrees (0/90/180/270), got RotZ={row.RotZ}";
                return false;
            }

            reason = null;
            return true;
        }

        private void RejectRow(long objectId, string reason)
        {
            if (!rejectedObjectIds.Add(objectId))
            {
                return;
            }

            Debug.LogWarning(
                $"[DecorStreamer] Skipping ObjectID {objectId}: {reason}. " +
                "Server footprint placer would reject this row (no collision); not rendering it.",
                this);
        }

        private void DrainSpawnBudget()
        {
            if (pendingSpawn.Count == 0)
            {
                return;
            }

            var budget = Mathf.Max(1, instantiationsPerFrame);
            var spawned = 0;
            while (pendingSpawn.Count > 0 && spawned < budget)
            {
                var last = pendingSpawn.Count - 1;
                var row = pendingSpawn[last];
                pendingSpawn.RemoveAt(last);

                if (loaded.ContainsKey(row.ObjectId))
                {
                    continue;
                }

                if (!TryInstantiate(row))
                {
                    continue;
                }

                spawned++;
            }
        }

        private bool TryInstantiate(WorldDecorRow row)
        {
            // Re-check at spawn time so a raced/pending bad row never becomes visible geometry.
            if (!IsFootprintCompatible(row, out var rejectReason))
            {
                RejectRow(row.ObjectId, rejectReason);
                return false;
            }

            if (!decorList.TryGet(row.DecorTypeId, out var decorType) || decorType == null)
            {
                Debug.LogWarning($"[DecorStreamer] Unknown DecorTypeId {row.DecorTypeId} for ObjectID {row.ObjectId}.", this);
                return false;
            }

            var prefab = decorType.Prefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[DecorStreamer] DecorType '{decorType.name}' has no Prefab.", this);
                return false;
            }

            var parent = runtimeRoot != null ? runtimeRoot : transform;
            var instance = Instantiate(
                prefab,
                new Vector3(row.PosX, row.PosY, 0f),
                Quaternion.Euler(0f, 0f, row.RotZ),
                parent);
            instance.name = $"{prefab.name}_{row.ObjectId}";
            instance.transform.localScale = new Vector3(row.Scale, row.Scale, row.Scale);

            // Streamed copies must not carry authoring-only DecorInstance (export markers).
            var authoring = instance.GetComponent<DecorInstance>();
            if (authoring != null)
            {
                Destroy(authoring);
            }

            loaded[row.ObjectId] = instance;
            return true;
        }

        private void DestroyLoaded(long objectId)
        {
            if (!loaded.TryGetValue(objectId, out var go))
            {
                return;
            }

            loaded.Remove(objectId);
            if (go != null)
            {
                Destroy(go);
            }
        }

        private void ClearAllInstances()
        {
            pendingSpawn.Clear();
            foreach (var pair in loaded)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            loaded.Clear();
        }
    }
}
