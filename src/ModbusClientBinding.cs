using System.Net;
using FluentModbus;

namespace Dantherm2Mqtt;
public class ModbusClientBinding : IModbusClient
{
	private readonly ModbusTcpClient _modbusClient;

	public ModbusClientBinding()
	{
		_modbusClient = new ModbusTcpClient();
	}

	public Task<Memory<byte>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count, CancellationToken cancellationToken)
	{
		// The * 2 is to convert points => bytes
		// According to fluentmodbus docs, it takes point (2 bytes) count as an arguments
		// But then it's not returning enough bytes
		return _modbusClient.ReadHoldingRegistersAsync<byte>(unitIdentifier, startingAddress, count * 2, cancellationToken);
	}

	public Task WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, byte[] dataset, CancellationToken cancellationToken = default)
	{
		return _modbusClient.WriteMultipleRegistersAsync(unitIdentifier, startingAddress, dataset, cancellationToken);
	}

	public void Connect(IPEndPoint remoteEndpoint, ModbusEndianness endianness)
	{
		_modbusClient.Connect(remoteEndpoint, endianness);
	}
}
