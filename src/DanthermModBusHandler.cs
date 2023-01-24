using System.Net;
using FluentModbus;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using Microsoft.Extensions.Options;
using ToMqttNet;
using MQTTnet;
using Newtonsoft.Json.Serialization;


public class DanthermModBusHandler : BackgroundService
{
	private readonly ILogger<DanthermModBusHandler> _logger;
	private readonly DanthermToMqttMetrics _metrics;
	private readonly IMqttConnectionService _mqtt;
	private short _addressOffset = -1;
	private ModbusTcpClient _modbusClient;
	private DanthermKind _result;
	private JsonSerializerSettings _jsonSettings;

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

		_jsonSettings = new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,
			ContractResolver = new DefaultContractResolver
			{
				NamingStrategy = new CamelCaseNamingStrategy()
			}
		};

		_jsonSettings.Converters.Add(new StringEnumConverter());
		_jsonSettings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
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
		var hasPublishedDiscovery = false;
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
						.WithPayload(JsonConvert.SerializeObject(_result, _jsonSettings))
						.Build());

					_metrics.UpdateMetrics(_result);
					_metrics.SetLastDataPull(_result, true);

					if(!hasPublishedDiscovery)
					{
						hasPublishedDiscovery = true;
						await PublishDiscoveryDocuments();
					}

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

	private async Task PublishDiscoveryDocuments()
	{
		var deviceName = "Dantherm HCV400";
		var availability = new List<MqttDiscoveryAvailablilty>{
			new MqttDiscoveryAvailablilty()
			{
				Topic = $"{_mqtt.MqttOptions.NodeId}/connected",
				PayloadAvailable = "2",
				PayloadNotAvailable = "0"
			}
		};
		var statusTopic = $"{_mqtt.MqttOptions.NodeId}/status/{_result.Status.SerialNum}";

		var device = new MqttDiscoveryDevice
		{
			Name = deviceName,
			Identifiers = new List<string>
			{
				_result.Status.SerialNum.ToString()
			}
		};


		var namingStrat = ((DefaultContractResolver)_jsonSettings.ContractResolver!).NamingStrategy!;
		var statusKey = namingStrat.GetPropertyName(nameof(DanthermKind.Status), false);

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Outdoor Temperature",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_outdoor_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.OutdoorTemperatureC), false)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Supply Temperature",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_supply_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.SupplyTemperatureC), false)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Extract Temperature",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_extract_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.ExtractTemperatureC), false)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Exhaust Temperature",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_exhaust_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.ExhaustTemperatureC), false)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Fan1 Speed",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_fan1_rpm",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.HALFan1Rpm), false)} | round(0) }}}}",
			UnitOfMeasurement = "rpm"
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Fan2 Speed",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_fan2_rpm",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.HALFan2Rpm), false)} | round(0) }}}}",
			UnitOfMeasurement = "rpm"
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Active Alarm",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_active_alarm",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.LastActiveAlarm), false)} }}}}"
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Work Hours",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_work_hours",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.WorkTimeHours), false)} }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TIME_HOURS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"{deviceName} - Remaining Filter Hours",
			UniqueId = $"dantherm_{_result.Status.SerialNum}_remaning_filter_hours",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ value_json.{statusKey}.{namingStrat.GetPropertyName(nameof(DanthermUvcStatus.FilterRemaningTimeDays), false)} }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TIME_DAYS.Value
		});
	}
}