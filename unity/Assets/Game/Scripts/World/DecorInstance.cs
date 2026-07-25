using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Scene-authored decor placement. Holds the catalog type; export writes its Id into world.db.
    /// </summary>
    [DisallowMultipleComponent]
    [SelectionBase]
    public sealed class DecorInstance : MonoBehaviour
    {
        [SerializeField]
        private DecorType decorType;

        public DecorType DecorType => decorType;

        /// <summary>Catalog id, or 0 when no type is assigned.</summary>
        public int Id => decorType != null ? decorType.Id : 0;

        public bool HasType => decorType != null;
    }
}
