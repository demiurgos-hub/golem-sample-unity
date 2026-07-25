using GolemEngine.Unity;
using UnityEngine;

namespace GolemSample.Unity
{
    [GolemEntity("Monster", Persistent = false)]
    public sealed class MonsterAuthoring : MonoBehaviour
    {
        [GolemVar(1, GolemSync.Once)]
        public uint MonsterTypeId;

        [GolemVar(2)]
        public int Health;
    }
}
