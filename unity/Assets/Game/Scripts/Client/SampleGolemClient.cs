using GolemEngine.Unity;
using GolemSample.GolemGenerated;
using UnityEngine;
using WebTransport;

namespace GolemSample.Unity
{
    public sealed class SampleGolemClient : GolemClientBehaviour
    {
        [SerializeField] private GolemEntitySpawner spawner;

        protected override GameClient CreateClient()
        {
            var client = ClientFactory.CreateClient(() => new GolemWebTransportTransport(new WebTransportClientOptions
            {
                AllowUntrustedCertificates = true
            }));
            spawner?.Bind(client.Entities);
            return client;
        }
    }
}
