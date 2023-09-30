using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using ToMqttNet;

namespace Dantherm2Mqtt.Test;

public class MqttConnectionServiceMock : IMqttConnectionService
{
    public MqttConnectionOptions MqttOptions => new MqttConnectionOptions();

    public List<MqttApplicationMessage> PublishedMessages { get; } = new ();

    public event EventHandler<MqttApplicationMessageReceivedEventArgs>? OnApplicationMessageReceived;
    public event EventHandler<EventArgs>? OnConnect;
    public event EventHandler<EventArgs>? OnDisconnect;

    public Task PublishAsync(MqttApplicationMessage applicationMessages)
    {
        PublishedMessages.Add(applicationMessages);
        return Task.CompletedTask;
	}

    public Task SubscribeAsync(params MqttTopicFilter[] topics)
    {
        throw new NotImplementedException();
    }

    public Task UnsubscribeAsync(params string[] topics)
    {
        throw new NotImplementedException();
    }
}
