using System.IO;
using UnityEditor;
using UnityEngine;

namespace GolemSample.Unity.Editor
{
    /// <summary>
    /// Keeps NuGetForUnity SQLite assemblies and natives Editor-only so Game.Runtime / player
    /// builds cannot pick them up until increment 11 adds a deliberate runtime dependency.
    /// </summary>
    [InitializeOnLoad]
    internal static class SqliteEditorOnlyPlugins
    {
        private static readonly string[] PackagePrefixes =
        {
            "Assets/Packages/Microsoft.Data.Sqlite",
            "Assets/Packages/SQLitePCLRaw",
        };

        static SqliteEditorOnlyPlugins()
        {
            EditorApplication.delayCall += () => Apply();
        }

        [MenuItem("Game/World/Mark SQLite Plugins Editor-Only")]
        private static void ApplyFromMenu()
        {
            var count = Apply();
            Debug.Log($"[SqliteEditorOnlyPlugins] Updated {count} plugin importer(s).");
        }

        private static int Apply()
        {
            if (!Directory.Exists(Path.Combine(Application.dataPath, "Packages")))
            {
                return 0;
            }

            var changed = 0;
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { "Assets/Packages" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsSqlitePackagePath(path))
                {
                    continue;
                }

                if (!path.EndsWith(".dll") && !path.EndsWith(".so") && !path.EndsWith(".dylib") &&
                    !path.EndsWith(".bundle"))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                if (importer == null)
                {
                    continue;
                }

                var any = importer.GetCompatibleWithAnyPlatform();
                var editor = importer.GetCompatibleWithEditor();
                var anyPlayerPlatform = false;
                foreach (BuildTarget target in System.Enum.GetValues(typeof(BuildTarget)))
                {
                    if (target == BuildTarget.NoTarget)
                    {
                        continue;
                    }

                    try
                    {
                        if (importer.GetCompatibleWithPlatform(target))
                        {
                            anyPlayerPlatform = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Some BuildTarget values are obsolete / unsupported.
                    }
                }

                if (!any && editor && !anyPlayerPlatform)
                {
                    continue;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                foreach (BuildTarget target in System.Enum.GetValues(typeof(BuildTarget)))
                {
                    if (target == BuildTarget.NoTarget)
                    {
                        continue;
                    }

                    try
                    {
                        importer.SetCompatibleWithPlatform(target, false);
                    }
                    catch
                    {
                        // Some BuildTarget values are obsolete / unsupported.
                    }
                }

                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }

        private static bool IsSqlitePackagePath(string path)
        {
            foreach (var prefix in PackagePrefixes)
            {
                if (path.StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
