using System;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;

namespace AbsoluteZero.Tests
{
    public sealed class TransportReceiveRecoveryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void UdpReceiveBuffers_AreReusableAfterEmptyDatagrams(bool sendEmptyDatagrams)
        {
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithNetworkConfigParameters(receiveQueueCapacity: 4, sendQueueCapacity: 4);
            var server = NetworkDriver.Create(settings);
            var client = NetworkDriver.Create();
            settings.Dispose();
            try
            {
                Assert.That(server.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(0)), Is.Zero);
                Assert.That(server.Listen(), Is.Zero);
                server.ScheduleUpdate().Complete();
                var endpoint = server.GetLocalEndpoint();
                if (sendEmptyDatagrams)
                {
                    using var socket = new UdpClient();
                    for (int i = 0; i < 8; i++)
                    {
                        socket.Send(Array.Empty<byte>(), 0, "127.0.0.1", endpoint.Port);
                        Thread.Sleep(10);
                        server.ScheduleUpdate().Complete();
                    }
                }
                client.Connect(endpoint);
                bool accepted = false;
                for (int i = 0; i < 200; i++)
                {
                    client.ScheduleUpdate().Complete();
                    server.ScheduleUpdate().Complete();
                    if (server.Accept().IsCreated) { accepted = true; break; }
                    Thread.Sleep(5);
                }
                Assert.That(accepted, Is.True, "UDP receive capacity was lost after completed empty datagrams");
            }
            finally
            {
                client.Dispose();
                server.Dispose();
            }
        }
    }
}
