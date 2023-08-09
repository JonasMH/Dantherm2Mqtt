using FluentModbus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using System.Net;
using ToMqttNet;

namespace Dantherm2Mqtt.Test;

public class DanthermModBusHandlerTests
{

	[Fact]
	public async Task Test1()
	{
		var loggerMock = new LoggerMock<DanthermModBusHandler>();
		var mqttMock = new MqttConnectionServiceMock();
		var modbusMock = new ModbusMock();
		var sut = new DanthermModBusHandler(
			loggerMock,
			null,
			modbusMock,
			Options.Create(new DanthermUvcSpec()),
			mqttMock,
			new DanthermTopicHelper(mqttMock));


		await sut.WriteHoldingRegistersAsync(10, new byte[] { 1, 2, 3, 4 });

		var registerResult = await sut.ReadHoldingRegistersAsync(10, 2);

		Assert.Equal(4, registerResult.Length);
		Assert.Equal(1, registerResult[0]);
		Assert.Equal(2, registerResult[1]);
		Assert.Equal(3, registerResult[2]);
		Assert.Equal(4, registerResult[3]);
	}
}
public class ModbusMock : IModbusClient
{
    public byte[] RegistryBuffer = new byte[1024];

    public void Connect(IPEndPoint remoteEndpoint, ModbusEndianness endianness)
    {
        throw new NotImplementedException();
    }

    public Task<Memory<byte>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[count * 2];
        Array.Copy(RegistryBuffer, startingAddress, buffer, 0, count * 2);
        return Task.FromResult(new Memory<byte>(buffer));
    }

    public Task WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, byte[] dataset, CancellationToken cancellationToken = default)
    {
        Array.Copy(dataset, 0, RegistryBuffer, startingAddress, dataset.Length);
        return Task.CompletedTask;
    }
}
public class MqttConnectionServiceMock : IMqttConnectionService
{
    public MqttConnectionOptions MqttOptions => throw new NotImplementedException();

    public event EventHandler<MqttApplicationMessageReceivedEventArgs>? OnApplicationMessageReceived;
    public event EventHandler<EventArgs>? OnConnect;
    public event EventHandler<EventArgs>? OnDisconnect;

    public Task PublishAsync(MqttApplicationMessage applicationMessages)
    {
        throw new NotImplementedException();
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

public class LoggerMock<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {

    }
}