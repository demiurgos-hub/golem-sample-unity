using GolemEngine.Unity;
using UnityEngine;

namespace GolemSample.Unity
{
    [CreateAssetMenu(fileName = "DecorType", menuName = "Golem Sample/Decor Type")]
    [GolemCatalog("Id")]
    public sealed class DecorType : ScriptableObject
    {
        // int key avoids a golem-bake C# bug with Dictionary<uint,_> map decode (`var key = 0`).
        [GolemField(1)]
        public int Id;

        [GolemField(2)]
        public string Name;

        [GolemField(3)]
        public bool Blocking;

        /// <summary>
        /// Prefab reference exported as the asset GUID — the same key footprint.Set.LookupGUID uses.
        /// </summary>
        [GolemAssetRef(4)]
        public GameObject Prefab;
    }
}
