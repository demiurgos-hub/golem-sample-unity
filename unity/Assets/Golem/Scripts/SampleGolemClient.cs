using GolemEngine.Unity;
using GolemSample.GolemGenerated;
using UnityEngine;

namespace GolemSample.Unity
{
    public sealed class SampleGolemClient : GolemClientBehaviour
    {
        [SerializeField] private GolemEntitySpawner spawner;

        protected override GameClient CreateClient()
        {
            var client = ClientFactory.CreateClient();
            spawner?.Bind(client.Entities);
            return client;
        }
    }
}
