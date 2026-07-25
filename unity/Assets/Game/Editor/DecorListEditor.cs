using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GolemSample.Unity.Editor
{
    [CustomEditor(typeof(DecorList))]
    public sealed class DecorListEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var list = (DecorList)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh from DecorType assets"))
            {
                Refresh(list);
            }
        }

        private static void Refresh(DecorList list)
        {
            var assets = FindDecorTypeAssets();
            var assetSet = new HashSet<DecorType>(assets);
            var previous = list.Decor;
            var report = new StringBuilder();

            for (var i = 0; i < previous.Count; i++)
            {
                var entry = previous[i];
                if (entry == null)
                {
                    report.AppendLine($"Null entry at list index {i}.");
                    continue;
                }

                if (!assetSet.Contains(entry))
                {
                    report.AppendLine($"List entry '{entry.name}' is missing from AssetDatabase t:DecorType scan.");
                }
            }

            foreach (var asset in assets)
            {
                var found = false;
                foreach (var entry in previous)
                {
                    if (entry == asset)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    report.AppendLine($"Asset '{asset.name}' ({AssetDatabase.GetAssetPath(asset)}) was missing from DecorList.");
                }
            }

            var next = new List<DecorType>();
            foreach (var entry in previous)
            {
                if (entry != null && assetSet.Contains(entry) && !next.Contains(entry))
                {
                    next.Add(entry);
                }
            }

            foreach (var asset in assets)
            {
                if (!next.Contains(asset))
                {
                    next.Add(asset);
                }
            }

            Undo.RecordObject(list, "Refresh DecorList");
            foreach (var entry in next)
            {
                if (entry != null)
                {
                    Undo.RecordObject(entry, "Assign DecorType Id");
                }
            }

            list.ReplaceDecor(next);
            var assigned = list.AssignMissingIds();
            if (assigned > 0)
            {
                report.AppendLine($"Assigned Ids to {assigned} new DecorType asset(s).");
                foreach (var entry in next)
                {
                    if (entry != null)
                    {
                        EditorUtility.SetDirty(entry);
                    }
                }
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            list.Validate(errors, warnings);
            foreach (var warning in warnings)
            {
                report.AppendLine("Warning: " + warning);
            }

            foreach (var error in errors)
            {
                report.AppendLine("Error: " + error);
            }

            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();

            if (errors.Count > 0)
            {
                Debug.LogError(
                    "DecorList refresh finished with errors:\n" + report,
                    list);
                EditorUtility.DisplayDialog("DecorList Refresh", report.ToString(), "OK");
                return;
            }

            var summary = report.Length == 0
                ? $"DecorList refreshed: {next.Count} DecorType asset(s), no divergence."
                : "DecorList refreshed:\n" + report;
            Debug.Log(summary, list);
            EditorUtility.DisplayDialog("DecorList Refresh", summary, "OK");
        }

        /// <summary>
        /// Same discovery scan Scribe uses: AssetDatabase.FindAssets("t:DecorType"),
        /// then filter to the exact runtime type and order by asset path.
        /// </summary>
        private static List<DecorType> FindDecorTypeAssets()
        {
            return AssetDatabase.FindAssets("t:DecorType")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<DecorType>)
                .Where(asset => asset != null)
                .OrderBy(asset => AssetDatabase.GetAssetPath(asset) ?? string.Empty)
                .ToList();
        }
    }
}
