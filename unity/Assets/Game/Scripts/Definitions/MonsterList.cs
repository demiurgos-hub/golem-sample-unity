using System.Collections.Generic;
using UnityEngine;

namespace GolemSample.Unity
{
    [CreateAssetMenu(fileName = "MonsterList", menuName = "Golem Sample/Monster List")]
    public sealed class MonsterList : ScriptableObject
    {
        [SerializeField]
        private List<MonsterType> monsters = new List<MonsterType>();

        private Dictionary<int, MonsterType> byId;

        public IReadOnlyList<MonsterType> Monsters => monsters;

        public bool TryGet(uint id, out MonsterType monsterType)
        {
            return TryGet((int)id, out monsterType);
        }

        public bool TryGet(int id, out MonsterType monsterType)
        {
            // In the Editor outside play mode, rebuild every access so inspector edits to
            // MonsterType.Id cannot leave a stale cache. At runtime, definitions are immutable.
            if (!Application.isPlaying)
            {
                return BuildLookup().TryGetValue(id, out monsterType);
            }

            EnsureLookup();
            return byId.TryGetValue(id, out monsterType);
        }

        /// <summary>
        /// Assigns a stable non-zero Id only when unset (0). Never renumbers existing entries,
        /// so reordering the list cannot change persisted MonsterTypeId protocol values.
        /// </summary>
        public int AssignMissingIds()
        {
            var used = new HashSet<int>();
            foreach (var monster in monsters)
            {
                if (monster != null && monster.Id != 0)
                {
                    used.Add(monster.Id);
                }
            }

            var next = 1;
            var assigned = 0;
            foreach (var monster in monsters)
            {
                if (monster == null || monster.Id != 0)
                {
                    continue;
                }

                while (used.Contains(next))
                {
                    next++;
                }

                monster.Id = next;
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

            for (var i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (monster == null)
                {
                    errors.Add($"MonsterList entry [{i}] is null.");
                    continue;
                }

                if (monster.Id <= 0)
                {
                    errors.Add($"MonsterType '{monster.name}' has Id {monster.Id} (must be > 0).");
                    continue;
                }

                assignedCount++;
                if (monster.Id > maxId)
                {
                    maxId = monster.Id;
                }

                if (seen.TryGetValue(monster.Id, out var other))
                {
                    errors.Add($"Duplicate MonsterType Id {monster.Id}: '{other}' and '{monster.name}'.");
                }
                else
                {
                    seen[monster.Id] = monster.name;
                }
            }

            if (assignedCount > 0 && maxId > assignedCount)
            {
                warnings.Add(
                    $"MonsterType Ids have gaps (max Id {maxId}, {assignedCount} assigned). " +
                    "Gaps are allowed so retired types keep stable protocol ids.");
            }
        }

        public void ReplaceMonsters(List<MonsterType> next)
        {
            monsters = next ?? new List<MonsterType>();
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

        private Dictionary<int, MonsterType> BuildLookup()
        {
            var lookup = new Dictionary<int, MonsterType>();
            if (monsters == null)
            {
                return lookup;
            }

            foreach (var monster in monsters)
            {
                if (monster == null || monster.Id <= 0 || lookup.ContainsKey(monster.Id))
                {
                    continue;
                }

                lookup[monster.Id] = monster;
            }

            return lookup;
        }

        private void InvalidateLookup()
        {
            byId = null;
        }
    }
}
