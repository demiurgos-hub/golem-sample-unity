using GolemSample.GolemGenerated;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Resolves a monster's catalog Visual through MonsterList and instances it under the entity.
    /// </summary>
    public sealed class MonsterVisual : MonsterView
    {
        [SerializeField]
        private MonsterList monsterList;

        private GameObject visualInstance;
        private SpriteRenderer rootRenderer;

        protected override void OnSpawned(SyncedMonster entity)
        {
            ApplyVisual(entity.MonsterTypeId);
        }

        protected override void OnDespawned(SyncedMonster entity)
        {
            ClearVisual();
        }

        private void ApplyVisual(uint monsterTypeId)
        {
            ClearVisual();

            if (monsterList == null || !monsterList.TryGet(monsterTypeId, out var type) || type == null)
            {
                Debug.LogWarning($"MonsterVisual: no MonsterType for id {monsterTypeId} on {name}.", this);
                return;
            }

            if (type.Visual == null)
            {
                Debug.LogWarning($"MonsterVisual: MonsterType '{type.Name}' (id {monsterTypeId}) has no Visual.", this);
                return;
            }

            if (rootRenderer == null)
            {
                rootRenderer = GetComponent<SpriteRenderer>();
            }

            if (rootRenderer != null)
            {
                rootRenderer.enabled = false;
            }

            visualInstance = Instantiate(type.Visual, transform);
            visualInstance.name = type.Visual.name;
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;
        }

        private void ClearVisual()
        {
            if (visualInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(visualInstance);
                }
                else
                {
                    DestroyImmediate(visualInstance);
                }

                visualInstance = null;
            }

            if (rootRenderer != null)
            {
                rootRenderer.enabled = true;
            }
        }
    }
}
