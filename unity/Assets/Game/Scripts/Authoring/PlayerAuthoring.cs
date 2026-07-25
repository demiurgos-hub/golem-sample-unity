using GolemEngine.Unity;
using UnityEngine;

namespace GolemSample.Unity
{
    [GolemEntity("Player")]
    public sealed class PlayerAuthoring : MonoBehaviour
    {
        [GolemVar(1)]
        public int Health;
    }
}
