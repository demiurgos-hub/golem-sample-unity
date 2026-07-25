using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GolemSample.Unity.Editor
{
    [CustomEditor(typeof(MonsterList))]
    public sealed class MonsterListEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var list = (MonsterList)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh from MonsterType assets"))
            {
                Refresh(list);
            }
        }

        private static void Refresh(MonsterList list)
        {
            var assets = FindMonsterTypeAssets();
            var assetSet = new HashSet<MonsterType>(assets);
            var previous = list.Monsters;
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
                    report.AppendLine($"List entry '{entry.name}' is missing from AssetDatabase t:MonsterType scan.");
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
                    report.AppendLine($"Asset '{asset.name}' ({AssetDatabase.GetAssetPath(asset)}) was missing from MonsterList.");
                }
            }

            var next = new List<MonsterType>();
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

            Undo.RecordObject(list, "Refresh MonsterList");
            foreach (var monster in next)
            {
                if (monster != null)
                {
                    Undo.RecordObject(monster, "Assign MonsterType Id");
                }
            }

            list.ReplaceMonsters(next);
            var assigned = list.AssignMissingIds();
            if (assigned > 0)
            {
                report.AppendLine($"Assigned Ids to {assigned} new MonsterType asset(s).");
                foreach (var monster in next)
                {
                    if (monster != null)
                    {
                        EditorUtility.SetDirty(monster);
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
                    "MonsterList refresh finished with errors:\n" + report,
                    list);
                EditorUtility.DisplayDialog("MonsterList Refresh", report.ToString(), "OK");
                return;
            }

            var summary = report.Length == 0
                ? $"MonsterList refreshed: {next.Count} MonsterType asset(s), no divergence."
                : "MonsterList refreshed:\n" + report;
            Debug.Log(summary, list);
            EditorUtility.DisplayDialog("MonsterList Refresh", summary, "OK");
        }

        /// <summary>
        /// Same discovery scan Scribe uses: AssetDatabase.FindAssets("t:MonsterType"),
        /// then filter to the exact runtime type and order by asset path.
        /// </summary>
        private static List<MonsterType> FindMonsterTypeAssets()
        {
            return AssetDatabase.FindAssets("t:MonsterType")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterType>)
                .Where(asset => asset != null)
                .OrderBy(asset => AssetDatabase.GetAssetPath(asset) ?? string.Empty)
                .ToList();
        }
    }
}
