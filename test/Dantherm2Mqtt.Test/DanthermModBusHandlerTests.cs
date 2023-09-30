using FluentModbus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dantherm2Mqtt.Test;

public class DanthermModBusHandlerTests
{
    private MqttConnectionServiceMock _mqttMock;
	private DanthermModBusHandler sut;

	public DanthermModBusHandlerTests()
	{
		var loggerMock = new LoggerMock<DanthermModBusHandler>();
		_mqttMock = new MqttConnectionServiceMock();
		var modbusMock = new ModbusMock();
		sut = new DanthermModBusHandler(
			loggerMock,
			null,
			modbusMock,
			Options.Create(new DanthermUvcSpec()),
			_mqttMock,
			new DanthermTopicHelper(_mqttMock));
	}


	[Fact]
	public async Task Test1()
	{
		await sut.WriteHoldingRegistersAsync(10, new byte[] { 1, 2, 3, 4 });

		var registerResult = await sut.ReadHoldingRegistersAsync(10, 2);

		Assert.Equal(4, registerResult.Length);
		Assert.Equal(1, registerResult[0]);
		Assert.Equal(2, registerResult[1]);
		Assert.Equal(3, registerResult[2]);
		Assert.Equal(4, registerResult[3]);
	}

    [Fact]
    public async Task ShouldPublishDiscoveryWithRightKeys()
    {
        await sut.PublishDiscoveryDocumentsAsync(new DanthermKind()
        {
            Spec = new DanthermUvcSpec() {},
            Status = new DanthermUvcStatus() {
                SystemId = new DanthermUvcSystemId(),
                SerialNum = 1337
            }
        });

        var discoveryDocs = _mqttMock.PublishedMessages.Select(x => JsonSerializer.Deserialize<JsonObject>(x.PayloadSegment)!).ToList();

        var outdootTemp = discoveryDocs.Single(x => x["unique_id"]?.ToString() == "dantherm_1337_outdoor_temp");

        Assert.Equal("{{ value_json.status.outdoorTemperatureC | round(1) }}", outdootTemp["value_template"]!.ToString());
    }

    [Fact]
    public void ShouldSerializeCorrectly()
    {
        var value = new DanthermKind()
        {
            Spec = new DanthermUvcSpec() { },
            Status = new DanthermUvcStatus()
            {
                SystemId = new DanthermUvcSystemId(),
                SerialNum = 1337
            }
        };

        var result = JsonSerializer.Serialize(value, DanthermMqttJsonContext.Default.DanthermKind);

        var deserialized = JsonSerializer.Deserialize<JsonObject>(result)!;

        Assert.Equal("1337", deserialized["status"]!["serialNum"]!.ToString());
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