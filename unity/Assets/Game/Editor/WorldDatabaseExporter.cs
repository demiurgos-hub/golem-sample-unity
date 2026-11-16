using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolemSample.Unity.Editor
{
    /// <summary>
    /// Scans authored <see cref="DecorInstance"/> components and rewrites
    /// Assets/StreamingAssets/world.db for the Go server and Unity client to stream later.
    /// </summary>
    public static class WorldDatabaseExporter
    {
        public const string MenuPath = "Game/World/Export World Database";
        public const string DatabaseAssetPath = "Assets/StreamingAssets/world.db";
        public const string TableName = "WorldDecorations";

        private const string DecorListFilter = "t:DecorList";
        private const float QuarterTurnEpsilon = 0.01f;
        private const float UniformScaleEpsilon = 1e-4f;

        [MenuItem(MenuPath)]
        public static void ExportFromMenu()
        {
            try
            {
                var result = ExportActiveScene();
                EditorUtility.DisplayDialog(
                    "World Database Export",
                    $"Exported {result.RowCount} decor row(s) to {DatabaseAssetPath}.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldDatabaseExporter] {ex.Message}");
                EditorUtility.DisplayDialog("World Database Export Failed", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Exports DecorInstance components from loaded scenes into world.db.
        /// Throws on validation failure so callers (and tests) can assert exact messages.
        /// </summary>
        public static ExportResult ExportActiveScene()
        {
            SQLitePCL.Batteries_V2.Init();

            var decorList = LoadDecorList();
            var instances = CollectInstances();
            var rows = new List<DecorRow>(instances.Count);
            var errors = new List<string>();

            foreach (var instance in instances)
            {
                if (!TryBuildRow(instance, decorList, out var row, out var error))
                {
                    errors.Add(error);
                    continue;
                }

                rows.Add(row);
            }

            if (errors.Count > 0)
            {
                var message = new StringBuilder();
                message.AppendLine($"World database export failed with {errors.Count} error(s):");
                foreach (var error in errors)
                {
                    message.AppendLine(error);
                }

                throw new InvalidOperationException(message.ToString().TrimEnd());
            }

            // Deterministic ObjectIDs: ordinal path, then sibling-index path (total order; List.Sort is not stable).
            rows = rows
                .OrderBy(r => r.SortKey, StringComparer.Ordinal)
                .ThenBy(r => r.SiblingIndexPath, StringComparer.Ordinal)
                .ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].ObjectId = i + 1;
            }

            var absolutePath = GetAbsoluteDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            WriteDatabase(absolutePath, rows);

            AssetDatabase.ImportAsset(DatabaseAssetPath);
            Debug.Log(
                $"[WorldDatabaseExporter] Wrote {rows.Count} row(s) to {DatabaseAssetPath} " +
                $"from {SceneManager.GetActiveScene().path}.");

            return new ExportResult(rows.Count, absolutePath);
        }

        public readonly struct ExportResult
        {
            public ExportResult(int rowCount, string absolutePath)
            {
                RowCount = rowCount;
                AbsolutePath = absolutePath;
            }

            public int RowCount { get; }
            public string AbsolutePath { get; }
        }

        private sealed class DecorRow
        {
            public int ObjectId;
            public int DecorTypeId;
            public float PosX;
            public float PosY;
            public float RotZ;
            public float Scale;
            public string SortKey;
            public string SiblingIndexPath;
        }

        private static List<DecorInstance> CollectInstances()
        {
            var found = UnityEngine.Object.FindObjectsByType<DecorInstance>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            // Prefer World/Authoring when present so runtime-spawned roots are never exported.
            // Authoring is inactive by default (no play-mode leak); GameObject.Find cannot see it.
            var authoringRoot = FindAuthoringRoot();
            if (authoringRoot == null)
            {
                return found.ToList();
            }

            if (!TryValidateIdentityAncestors(authoringRoot.transform, out var ancestorError))
            {
                throw new InvalidOperationException(ancestorError);
            }

            return found
                .Where(instance => instance != null && instance.transform.IsChildOf(authoringRoot.transform))
                .ToList();
        }

        /// <summary>
        /// Locates World/Authoring even when the subtree is inactive (authoring-only, disabled in play).
        /// </summary>
        private static GameObject FindAuthoringRoot()
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != "World")
                    {
                        continue;
                    }

                    for (var i = 0; i < root.transform.childCount; i++)
                    {
                        var child = root.transform.GetChild(i);
                        if (child.name == "Authoring")
                        {
                            return child.gameObject;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// World-space export equals local placement only when Authoring and its ancestors are identity.
        /// </summary>
        private static bool TryValidateIdentityAncestors(Transform authoringRoot, out string error)
        {
            error = null;
            const float epsilon = 1e-5f;
            for (var t = authoringRoot; t != null; t = t.parent)
            {
                var path = GetHierarchyPath(t);
                if (t.localPosition.sqrMagnitude > epsilon * epsilon)
                {
                    error =
                        $"Authoring ancestor '{path}' must have identity localPosition " +
                        $"(got {t.localPosition}); exported PosX/PosY are world-space.";
                    return false;
                }

                if (Quaternion.Angle(t.localRotation, Quaternion.identity) > QuarterTurnEpsilon)
                {
                    error =
                        $"Authoring ancestor '{path}' must have identity localRotation " +
                        $"(got euler {t.localRotation.eulerAngles}); exported coordinates are world-space.";
                    return false;
                }

                var s = t.localScale;
                if (Mathf.Abs(s.x - 1f) > epsilon || Mathf.Abs(s.y - 1f) > epsilon || Mathf.Abs(s.z - 1f) > epsilon)
                {
                    error =
                        $"Authoring ancestor '{path}' must have identity localScale " +
                        $"(got {s}); exported coordinates are world-space.";
                    return false;
                }
            }

            return true;
        }

        private static DecorList LoadDecorList()
        {
            var guids = AssetDatabase.FindAssets(DecorListFilter);
            if (guids.Length == 0)
            {
                throw new InvalidOperationException("No DecorList asset found. Create one under Assets/Game/Decor.");
            }

            if (guids.Length > 1)
            {
                var paths = guids.Select(AssetDatabase.GUIDToAssetPath);
                throw new InvalidOperationException(
                    "Expected exactly one DecorList asset, found:\n" + string.Join("\n", paths));
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var list = AssetDatabase.LoadAssetAtPath<DecorList>(path);
            if (list == null)
            {
                throw new InvalidOperationException($"Failed to load DecorList at '{path}'.");
            }

            return list;
        }

        private static bool TryBuildRow(
            DecorInstance instance,
            DecorList decorList,
            out DecorRow row,
            out string error)
        {
            row = default;
            error = null;
            var go = instance.gameObject;
            var path = GetHierarchyPath(go.transform);

            if (!instance.HasType)
            {
                error = $"DecorInstance on '{path}' has no DecorType assigned.";
                return false;
            }

            var type = instance.DecorType;
            if (type.Id <= 0)
            {
                error =
                    $"DecorInstance on '{path}' references DecorType '{type.name}' with Id {type.Id} " +
                    "(must be > 0).";
                return false;
            }

            if (!decorList.TryGet(type.Id, out var listed) || listed != type)
            {
                error =
                    $"DecorInstance on '{path}' references DecorType '{type.name}' (Id {type.Id}) " +
                    "which is not present in DecorList.";
                return false;
            }

            var t = go.transform;
            if (!TryGetUniformPositiveScale(t, out var scale, out var scaleError))
            {
                error = $"DecorInstance on '{path}': {scaleError}";
                return false;
            }

            if (!TryGetQuarterTurnDegrees(t, out var rotZ, out var rotError))
            {
                error = $"DecorInstance on '{path}': {rotError}";
                return false;
            }

            // World-space; CollectInstances requires World/Authoring ancestors to be identity transforms.
            var pos = t.position;
            row = new DecorRow
            {
                ObjectId = 0,
                DecorTypeId = type.Id,
                PosX = pos.x,
                PosY = pos.y,
                RotZ = rotZ,
                Scale = scale,
                SortKey = path,
                SiblingIndexPath = GetSiblingIndexPath(t),
            };
            return true;
        }

        private static bool TryGetUniformPositiveScale(Transform t, out float scale, out string error)
        {
            var s = t.lossyScale;
            scale = s.x;
            error = null;

            if (!(s.x > 0f) || !(s.y > 0f) || !(s.z > 0f))
            {
                error =
                    $"scale must be positive and uniform (got lossyScale=({Format(s.x)}, {Format(s.y)}, {Format(s.z)})).";
                return false;
            }

            if (Mathf.Abs(s.x - s.y) > UniformScaleEpsilon || Mathf.Abs(s.x - s.z) > UniformScaleEpsilon)
            {
                error =
                    $"scale must be uniform (got lossyScale=({Format(s.x)}, {Format(s.y)}, {Format(s.z)})). " +
                    "The world database stores a single positive Scale.";
                return false;
            }

            return true;
        }

        private static bool TryGetQuarterTurnDegrees(Transform t, out float degrees, out string error)
        {
            // 2D: only Z rotation is authored; reject non-zero X/Y pitch/roll.
            var euler = t.rotation.eulerAngles;
            degrees = 0f;
            error = null;

            if (Mathf.Abs(NormalizeDegrees(euler.x)) > QuarterTurnEpsilon ||
                Mathf.Abs(NormalizeDegrees(euler.y)) > QuarterTurnEpsilon)
            {
                error =
                    $"rotation must be a pure Z quarter-turn (got euler=({Format(euler.x)}, {Format(euler.y)}, {Format(euler.z)})).";
                return false;
            }

            var z = NormalizeDegrees(euler.z);
            float[] allowed = { 0f, 90f, 180f, 270f };
            foreach (var candidate in allowed)
            {
                if (Mathf.Abs(z - candidate) <= QuarterTurnEpsilon ||
                    (candidate == 0f && Mathf.Abs(z - 360f) <= QuarterTurnEpsilon))
                {
                    degrees = candidate;
                    return true;
                }
            }

            error =
                $"rotation Z must be an exact quarter turn (0/90/180/270), got {Format(euler.z)}°. " +
                "Object Template placement rejects other angles.";
            return false;
        }

        private static float NormalizeDegrees(float degrees)
        {
            var mod = degrees % 360f;
            if (mod < 0f)
            {
                mod += 360f;
            }

            return mod;
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string GetHierarchyPath(Transform t)
        {
            var parts = new Stack<string>();
            while (t != null)
            {
                parts.Push(t.name);
                t = t.parent;
            }

            return string.Join("/", parts);
        }

        /// <summary>
        /// Zero-padded sibling indices from root to leaf — total order when names collide.
        /// </summary>
        private static string GetSiblingIndexPath(Transform t)
        {
            var parts = new Stack<string>();
            while (t != null)
            {
                parts.Push(t.GetSiblingIndex().ToString("D4", CultureInfo.InvariantCulture));
                t = t.parent;
            }

            return string.Join("/", parts);
        }

        private static string GetAbsoluteDatabasePath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "world.db"));
        }

        private static void WriteDatabase(string absolutePath, List<DecorRow> rows)
        {
            // Delete+recreate so page layout and ObjectIDs are deterministic across exports.
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            var journal = absolutePath + "-journal";
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = absolutePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;";
                pragma.ExecuteNonQuery();
            }

            using (var create = connection.CreateCommand())
            {
                create.CommandText = $@"
CREATE TABLE {TableName} (
    ObjectID INTEGER PRIMARY KEY NOT NULL,
    DecorTypeId INTEGER NOT NULL,
    PosX REAL NOT NULL,
    PosY REAL NOT NULL,
    RotZ REAL NOT NULL,
    Scale REAL NOT NULL
);";
                create.ExecuteNonQuery();
            }

            using (var transaction = connection.BeginTransaction())
            {
                foreach (var row in rows)
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = $@"
INSERT INTO {TableName}
    (ObjectID, DecorTypeId, PosX, PosY, RotZ, Scale)
VALUES
    (@ObjectID, @DecorTypeId, @PosX, @PosY, @RotZ, @Scale);";
                    insert.Parameters.AddWithValue("@ObjectID", row.ObjectId);
                    insert.Parameters.AddWithValue("@DecorTypeId", row.DecorTypeId);
                    insert.Parameters.AddWithValue("@PosX", row.PosX);
                    insert.Parameters.AddWithValue("@PosY", row.PosY);
                    insert.Parameters.AddWithValue("@RotZ", row.RotZ);
                    insert.Parameters.AddWithValue("@Scale", row.Scale);
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            using (var vacuum = connection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
        }
    }
}
