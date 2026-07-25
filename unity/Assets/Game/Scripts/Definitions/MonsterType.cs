using GolemEngine.Unity;
using UnityEngine;

namespace GolemSample.Unity
{
    [CreateAssetMenu(fileName = "MonsterType", menuName = "Golem Sample/Monster Type")]
    [GolemCatalog("Id")]
    public sealed class MonsterType : ScriptableObject
    {
        // int key avoids a golem-bake C# bug with Dictionary<uint,_> map decode (`var key = 0`).
        [GolemField(1)]
        public int Id;

        [GolemField(2)]
        public string Name;

        [GolemField(3)]
        public int Health;

        [GolemField(4)]
        public uint Damage;

        [GolemField(5)]
        public float MoveSpeed;

        /// <summary>Unity-only visual prefab; not exported by Scribe.</summary>
        public GameObject Visual;
    }
}
