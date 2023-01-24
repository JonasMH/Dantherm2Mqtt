using System.Net;
using FluentModbus;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using Prometheus;
using Microsoft.Extensions.Options;
using ToMqttNet;
using MQTTnet;

public class DanthermToMqttMetrics
{
	private readonly MetricFactory _metricsFactory;
	private readonly Gauge _lastActiveAlarm;
	private readonly Counter _lastPull;

	public DanthermToMqttMetrics(MetricFactory metricsFactory)
	{
		_metricsFactory = metricsFactory;

		_lastActiveAlarm = _metricsFactory.CreateGauge("danthermtomqtt_last_active_alarm", "The last active alarm, zero = none, see Dantherm documentation if not zero", new GaugeConfiguration
		{
			LabelNames = new string[]
			{
				"device_serial"
			}
		});

		_lastPull = _metricsFactory.CreateCounter("danthermtomqtt_last_data_pull_time", "The last time data either failed or pulled successfully", new CounterConfiguration
		{
			LabelNames = new string[]
			{
				"succeeded",
				"device_serial"
			}
		});
	}

	public void UpdateMetrics(DanthermKind kind)
	{
		_lastActiveAlarm
			.WithLabels(kind.Status.SerialNum.ToString())
			.Set((int)kind.Status.LastActiveAlarm);
	}

	public void SetLastDataPull(DanthermKind kind, bool succeeded)
	{
		_lastPull
			.WithLabels(succeeded.ToString(), kind.Status.SerialNum.ToString())
			.IncToCurrentTimeUtc();
	}
}


public class DanthermModBusHandler : BackgroundService
{
	private readonly ILogger<DanthermModBusHandler> _logger;
	private readonly DanthermToMqttMetrics _metrics;
	private readonly IMqttConnectionService _mqtt;
	private short _addressOffset = -1;
	private ModbusTcpClient _modbusClient;
	private DanthermKind _result;

	public DanthermModBusHandler(
		ILogger<DanthermModBusHandler> logger,
		DanthermToMqttMetrics metrics,
		IOptions<DanthermUvcSpec> danthermOptions,
		IMqttConnectionService mqtt)
	{
		_logger = logger;
		_metrics = metrics;
		_mqtt = mqtt;
		_modbusClient = new ModbusTcpClient();
		_result = new DanthermKind()
		{
			Spec = danthermOptions.Value
		};
	}

	private async Task<byte[]> ReadHoldingRegistersAsync(ushort register, ushort points)
	{
		var data = (await _modbusClient.ReadHoldingRegistersAsync(_result.Spec.SlaveAddress, (ushort)(register + _addressOffset), points)).ToArray();
		var result = new byte[data.Length];

		for (int i = 0; i < data.Length; i += 2)
		{
			result[i] = data[i + 1];
			result[i+1] = data[i];
		}


		return result;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var jsonOptions = new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,

		};

		jsonOptions.Converters.Add(new StringEnumConverter());
		jsonOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				_logger.LogInformation("Trying to connect");

				// configure socket
				var serverFullAddr = new IPEndPoint(IPAddress.Parse(_result.Spec.Address), _result.Spec.Port);
				_modbusClient.Connect(serverFullAddr, ModbusEndianness.LittleEndian);
				_logger.LogInformation("Connected to socket");


				_result.Status.SystemId = DanthermUvcSystemId.Parse(await ReadHoldingRegistersAsync(3, 2));
				_result.Status.SystemName = Encoding.ASCII.GetString(await ReadHoldingRegistersAsync(9, 8));
				_result.Status.SerialNum = BitConverter.ToUInt64(await ReadHoldingRegistersAsync(5, 4));
				_result.Status.FWVersion = DanthermUvcFwVersion.Parse(await ReadHoldingRegistersAsync(25, 2));

				var macAddrData = await ReadHoldingRegistersAsync(41, 4);
				_result.Status.MacAddress = string.Join(":", (new byte[]{
						macAddrData[1],
						macAddrData[0],
						macAddrData[7],
						macAddrData[6],
						macAddrData[5],
						macAddrData[4],
					}
						.Select(x => Convert.ToHexString(new byte[]{ x}))));
				_result.Status.StartExploitation = Instant.FromUnixTimeSeconds(BitConverter.ToUInt32(await ReadHoldingRegistersAsync(669, 2)));

				while (true)
				{
					_result.Status.HalLeft = (await ReadHoldingRegistersAsync(85, 1))[0] == 1;
					_result.Status.HalRight = (await ReadHoldingRegistersAsync(87, 1))[0] == 1;
					_result.Status.DateTime = Instant.FromUnixTimeSeconds(BitConverter.ToUInt32(await ReadHoldingRegistersAsync(109, 2)));
					_result.Status.WorkTimeHours = BitConverter.ToUInt32(await ReadHoldingRegistersAsync(625, 2));
					_result.Status.CurrentBLState = (DanthermUvcModeOfOperation)(await ReadHoldingRegistersAsync(473, 2))[0];
					_result.Status.OutdoorTemperatureC = BitConverter.ToSingle(await ReadHoldingRegistersAsync(133, 2));
					_result.Status.SupplyTemperatureC = BitConverter.ToSingle(await ReadHoldingRegistersAsync(135, 2));
					_result.Status.ExtractTemperatureC = BitConverter.ToSingle(await ReadHoldingRegistersAsync(137, 2));
					_result.Status.ExhaustTemperatureC = BitConverter.ToSingle(await ReadHoldingRegistersAsync(139, 2));
					_result.Status.FilterRemaningTimeDays = BitConverter.ToUInt32(await ReadHoldingRegistersAsync(555, 2));
					_result.Status.LastActiveAlarm = (DanthermUvcAlarm)(await ReadHoldingRegistersAsync(517, 2))[0];
					_result.Status.HALFan1Rpm = BitConverter.ToSingle(await ReadHoldingRegistersAsync(101, 2));
					_result.Status.HALFan2Rpm= BitConverter.ToSingle(await ReadHoldingRegistersAsync(103, 2));

					await _mqtt.PublishAsync(
						new MqttApplicationMessageBuilder()
						.WithTopic($"{_mqtt.MqttOptions.NodeId}/status/{_result.Status.SerialNum}")
						.WithPayload(JsonConvert.SerializeObject(_result, jsonOptions))
						.Build());

					_metrics.UpdateMetrics(_result);
					_metrics.SetLastDataPull(_result, true);
					await Task.Delay(TimeSpan.FromMilliseconds(_result.Spec.PollingIntervalMS));
				}

			} catch(Exception ex)
			{
				_metrics.SetLastDataPull(_result, false);
				_logger.LogError(ex, "Failed to read from modbus device");
			}

			await Task.Delay(TimeSpan.FromMilliseconds(_result.Spec.PollingIntervalMS));
		}
	}
}