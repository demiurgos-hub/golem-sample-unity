using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GolemSample.Unity.Editor
{
    /// <summary>
    /// Restricts NuGetForUnity SQLite assemblies/natives to Editor + desktop standalone
    /// (Win / Win64 / Linux64 / OSX). Android, WebGL, consoles, and mobile stay disabled —
    /// StreamingAssets is not a direct SQLite path on those platforms anyway.
    /// </summary>
    [InitializeOnLoad]
    internal static class SqliteEditorOnlyPlugins
    {
        private static readonly string[] PackagePrefixes =
        {
            "Assets/Packages/Microsoft.Data.Sqlite",
            "Assets/Packages/SQLitePCLRaw",
        };

        private static readonly BuildTarget[] DesktopPlayerTargets =
        {
            BuildTarget.StandaloneWindows,
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneLinux64,
            BuildTarget.StandaloneOSX,
        };

        // Platforms we explicitly enable or keep disabled. Avoid iterating every
        // BuildTarget enum value — deprecated entries warn/fail on SetCompatibleWithPlatform.
        private static readonly BuildTarget[] TrackedPlayerTargets =
        {
            BuildTarget.StandaloneWindows,
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneLinux64,
            BuildTarget.StandaloneOSX,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.tvOS,
            BuildTarget.WebGL,
            BuildTarget.WSAPlayer,
            BuildTarget.PS4,
            BuildTarget.PS5,
            BuildTarget.Switch,
            BuildTarget.XboxOne,
            BuildTarget.GameCoreXboxOne,
            BuildTarget.GameCoreXboxSeries,
            BuildTarget.EmbeddedLinux,
            BuildTarget.VisionOS,
        };

        static SqliteEditorOnlyPlugins()
        {
            EditorApplication.delayCall += () => Apply();
        }

        [MenuItem("Game/World/Configure SQLite Plugins For Desktop")]
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
                if (!IsSqlitePackagePath(path) || !IsPluginBinary(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                if (importer == null)
                {
                    continue;
                }

                if (MatchesDesired(importer, path))
                {
                    continue;
                }

                ApplyDesired(importer, path);
                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }

        private static bool IsPluginBinary(string path)
        {
            return path.EndsWith(".dll") || path.EndsWith(".so") || path.EndsWith(".dylib") ||
                   path.EndsWith(".bundle");
        }

        private static bool MatchesDesired(PluginImporter importer, string path)
        {
            if (importer.GetCompatibleWithAnyPlatform())
            {
                return false;
            }

            GetDesired(path, out var wantEditor, out var wantPlayers);

            if (importer.GetCompatibleWithEditor() != wantEditor)
            {
                return false;
            }

            // Idempotency considers every tracked platform (desktop enabled set plus
            // mobile/console we must keep off) — not only the targets we turn on.
            foreach (var target in TrackedPlayerTargets)
            {
                bool actual;
                try
                {
                    actual = importer.GetCompatibleWithPlatform(target);
                }
                catch
                {
                    continue;
                }

                var desired = wantPlayers.Contains(target);
                if (actual != desired)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyDesired(PluginImporter importer, string path)
        {
            GetDesired(path, out var wantEditor, out var wantPlayers);

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(wantEditor);

            foreach (var target in TrackedPlayerTargets)
            {
                try
                {
                    importer.SetCompatibleWithPlatform(target, wantPlayers.Contains(target));
                }
                catch
                {
                    // Unsupported on this Editor version.
                }
            }

            ApplyNativeCpuHints(importer, path);
        }

        private static void GetDesired(string path, out bool wantEditor, out HashSet<BuildTarget> wantPlayers)
        {
            wantPlayers = new HashSet<BuildTarget>();
            var normalized = path.Replace('\\', '/');

            if (normalized.Contains("/runtimes/win-x64/"))
            {
                wantEditor = true;
                wantPlayers.Add(BuildTarget.StandaloneWindows64);
                return;
            }

            if (normalized.Contains("/runtimes/win-x86/"))
            {
                // 32-bit Windows player only; modern Editors are 64-bit.
                wantEditor = false;
                wantPlayers.Add(BuildTarget.StandaloneWindows);
                return;
            }

            if (normalized.Contains("/runtimes/linux-x64/"))
            {
                wantEditor = true;
                wantPlayers.Add(BuildTarget.StandaloneLinux64);
                return;
            }

            if (normalized.Contains("/runtimes/osx-"))
            {
                wantEditor = true;
                wantPlayers.Add(BuildTarget.StandaloneOSX);
                return;
            }

            // Managed Microsoft.Data.Sqlite / SQLitePCLRaw assemblies: Editor + desktop players.
            wantEditor = true;
            foreach (var target in DesktopPlayerTargets)
            {
                wantPlayers.Add(target);
            }
        }

        private static void ApplyNativeCpuHints(PluginImporter importer, string path)
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("/runtimes/win-x64/"))
            {
                importer.SetEditorData("CPU", "x86_64");
                importer.SetEditorData("OS", "Windows");
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            }
            else if (normalized.Contains("/runtimes/win-x86/"))
            {
                importer.SetPlatformData(BuildTarget.StandaloneWindows, "CPU", "x86");
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "None");
            }
            else if (normalized.Contains("/runtimes/linux-x64/"))
            {
                importer.SetEditorData("CPU", "x86_64");
                importer.SetEditorData("OS", "Linux");
                importer.SetPlatformData(BuildTarget.StandaloneLinux64, "CPU", "x86_64");
            }
            else if (normalized.Contains("/runtimes/osx-arm64/"))
            {
                importer.SetEditorData("OS", "OSX");
                importer.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "ARM64");
            }
            else if (normalized.Contains("/runtimes/osx-x64/"))
            {
                importer.SetEditorData("OS", "OSX");
                importer.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "x86_64");
            }
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
