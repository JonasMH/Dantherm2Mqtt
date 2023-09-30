using System.Net;
using FluentModbus;
namespace Dantherm2Mqtt;

public interface IModbusClient
{
	Task<Memory<byte>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count, CancellationToken cancellationToken = default);
	Task WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, byte[] dataset, CancellationToken cancellationToken = default);
	void Connect(IPEndPoint remoteEndpoint, ModbusEndianness endianness);
}
