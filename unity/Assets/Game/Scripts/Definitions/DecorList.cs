using System.Collections.Generic;
using UnityEngine;

namespace GolemSample.Unity
{
    [CreateAssetMenu(fileName = "DecorList", menuName = "Golem Sample/Decor List")]
    public sealed class DecorList : ScriptableObject
    {
        [SerializeField]
        private List<DecorType> decor = new List<DecorType>();

        private Dictionary<int, DecorType> byId;

        public IReadOnlyList<DecorType> Decor => decor;

        public bool TryGet(uint id, out DecorType decorType)
        {
            return TryGet((int)id, out decorType);
        }

        public bool TryGet(int id, out DecorType decorType)
        {
            // In the Editor outside play mode, rebuild every access so inspector edits to
            // DecorType.Id cannot leave a stale cache. At runtime, definitions are immutable.
            if (!Application.isPlaying)
            {
                return BuildLookup().TryGetValue(id, out decorType);
            }

            EnsureLookup();
            return byId.TryGetValue(id, out decorType);
        }

        /// <summary>
        /// Assigns a stable non-zero Id only when unset (0). Never renumbers existing entries,
        /// so reordering the list cannot change persisted DecorTypeId protocol values.
        /// </summary>
        public int AssignMissingIds()
        {
            var used = new HashSet<int>();
            foreach (var entry in decor)
            {
                if (entry != null && entry.Id != 0)
                {
                    used.Add(entry.Id);
                }
            }

            var next = 1;
            var assigned = 0;
            foreach (var entry in decor)
            {
                if (entry == null || entry.Id != 0)
                {
                    continue;
                }

                while (used.Contains(next))
                {
                    next++;
                }

                entry.Id = next;
                used.Add(next);
                next++;
                assigned++;
            }

            if (assigned > 0)
            {
                InvalidateLookup();
            }

            return assigned;
        }

        /// <summary>
        /// Validates null entries, zero ids, and duplicates. Gaps are reported as warnings:
        /// retired types may leave holes, and forcing contiguity would risk renumbering.
        /// </summary>
        public void Validate(List<string> errors, List<string> warnings)
        {
            errors ??= new List<string>();
            warnings ??= new List<string>();

            var seen = new Dictionary<int, string>();
            var maxId = 0;
            var assignedCount = 0;

            for (var i = 0; i < decor.Count; i++)
            {
                var entry = decor[i];
                if (entry == null)
                {
                    errors.Add($"DecorList entry [{i}] is null.");
                    continue;
                }

                if (entry.Id <= 0)
                {
                    errors.Add($"DecorType '{entry.name}' has Id {entry.Id} (must be > 0).");
                    continue;
                }

                assignedCount++;
                if (entry.Id > maxId)
                {
                    maxId = entry.Id;
                }

                if (seen.TryGetValue(entry.Id, out var other))
                {
                    errors.Add($"Duplicate DecorType Id {entry.Id}: '{other}' and '{entry.name}'.");
                }
                else
                {
                    seen[entry.Id] = entry.name;
                }
            }

            if (assignedCount > 0 && maxId > assignedCount)
            {
                warnings.Add(
                    $"DecorType Ids have gaps (max Id {maxId}, {assignedCount} assigned). " +
                    "Gaps are allowed so retired types keep stable protocol ids.");
            }
        }

        public void ReplaceDecor(List<DecorType> next)
        {
            decor = next ?? new List<DecorType>();
            InvalidateLookup();
        }

        private void OnEnable()
        {
            InvalidateLookup();
        }

        private void OnValidate()
        {
            InvalidateLookup();
        }

        private void EnsureLookup()
        {
            if (byId != null)
            {
                return;
            }

            byId = BuildLookup();
        }

        private Dictionary<int, DecorType> BuildLookup()
        {
            var lookup = new Dictionary<int, DecorType>();
            if (decor == null)
            {
                return lookup;
            }

            foreach (var entry in decor)
            {
                if (entry == null || entry.Id <= 0 || lookup.ContainsKey(entry.Id))
                {
                    continue;
                }

                lookup[entry.Id] = entry;
            }

            return lookup;
        }

        private void InvalidateLookup()
        {
            byId = null;
        }
    }
}
