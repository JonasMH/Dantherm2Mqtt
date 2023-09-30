using System.Net;
using FluentModbus;
using System.Text;
using NodaTime;
using Microsoft.Extensions.Options;
using ToMqttNet;
using MQTTnet;
using System.Linq.Expressions;
using System.Reflection;
using MQTTnet.Packets;
using MQTTnet.Client;
using System.Text.Json;
using System.Net.Http.Json;
namespace Dantherm2Mqtt;

public class DanthermModBusHandler : BackgroundService
{
	private readonly ILogger<DanthermModBusHandler> _logger;
	private readonly DanthermToMqttMetrics? _metrics;
	private readonly IMqttConnectionService _mqtt;
	private readonly DanthermTopicHelper _topicHelper;
	private short _addressOffset = -1;
	private IModbusClient _modbusClient;
	private DanthermKind _result;
	private DanthermMqttJsonContext _jsonContext = DanthermMqttJsonContext.Default;

	public DanthermModBusHandler(
		ILogger<DanthermModBusHandler> logger,
		DanthermToMqttMetrics? metrics,
		IModbusClient modBusClient,
		IOptions<DanthermUvcSpec> danthermOptions,
		IMqttConnectionService mqtt,
		DanthermTopicHelper topicHelper)
	{
		_logger = logger;
		_metrics = metrics;
		_mqtt = mqtt;
		_topicHelper = topicHelper;
		_modbusClient = modBusClient;
		_result = new DanthermKind()
		{
			Spec = danthermOptions.Value
		};
	}

	public async Task<byte[]> ReadHoldingRegistersAsync(ushort register, ushort points)
	{
		// The address offset of -1 is needed because something about 'PLC Addresses (Base 1)' (See docs)
		var data = (await _modbusClient.ReadHoldingRegistersAsync(_result.Spec.SlaveAddress, (ushort)(register + _addressOffset), points)).ToArray();
		var result = new byte[data.Length];

		for (int i = 0; i < data.Length; i += 2)
		{
			result[i] = data[i + 1];
			result[i+1] = data[i];
		}


		return result;
	}

	public async Task WriteHoldingRegistersAsync(ushort register, byte[] data)
	{
		var flippedData = new byte[data.Length];
		for (int i = 0; i < data.Length; i += 2)
		{
			flippedData[i] = data[i + 1];
			flippedData[i + 1] = data[i];
		}

		await _modbusClient.WriteMultipleRegistersAsync(_result.Spec.SlaveAddress, (ushort)(register + _addressOffset), flippedData);
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
				await ReadStaticValuesAsync();

				while (!stoppingToken.IsCancellationRequested)
				{
					await ReadDynamicValuesAsync();
					await _mqtt.PublishAsync(
						new MqttApplicationMessageBuilder()
						.WithTopic(_topicHelper.GetStatusTopic(_result.Status.SerialNum))
						.WithPayload(JsonSerializer.Serialize(_result, DanthermMqttJsonContext.Default.DanthermKind))
						.Build());

					_metrics?.UpdateMetrics(_result);

					// Only publish the discovery document after the first values have been published
					if(!hasPublishedDiscovery)
					{
						hasPublishedDiscovery = true;
						await SetupSubscriptionsAsync();
						await PublishDiscoveryDocumentsAsync(_result);
					}

					await Task.Delay(TimeSpan.FromMilliseconds(_result.Spec.PollingIntervalMS), stoppingToken);
				}

			} catch(Exception ex)
			{
				_logger.LogError(ex, "Failed to read from modbus device");
			}

			await Task.Delay(TimeSpan.FromMilliseconds(_result.Spec.PollingIntervalMS), stoppingToken);
		}
	}

	private async Task SetupSubscriptionsAsync()
	{
		await _mqtt.SubscribeAsync(new MqttTopicFilter()
		{
			Topic = _topicHelper.GetSetTopicRegex(_result.Status.SerialNum)
		});

		_mqtt.OnApplicationMessageReceived += async (sender, e) => await HandleMessageAsync(sender, e);
	}

	private async Task HandleMessageAsync(object? sender, MqttApplicationMessageReceivedEventArgs evnt)
	{
		try
		{
			var payload = evnt.ApplicationMessage.ConvertPayloadToString();
			if (evnt.ApplicationMessage.Topic == _topicHelper.GetSetUnitModeTopic(_result.Status.SerialNum))
			{
				_logger.LogInformation("Received set mode of operation command with payload {payload}", payload);
				var modeOfOperation = Enum.Parse<DanthermUvcSetModeOfOperation>(payload);
				await WriteHoldingRegistersAsync(169, BitConverter.GetBytes((uint)modeOfOperation));
			}

			if (evnt.ApplicationMessage.Topic == _topicHelper.GetSetFanSpeedLevel(_result.Status.SerialNum))
			{
				_logger.LogInformation("Received set fan speed level command with payload {payload}", payload);
				var speedLevel = int.Parse(payload);

				if(speedLevel < 0 || speedLevel > 4)
				{
					throw new DanthermException("Fan Speed Level must be Min: 0, Max: 4, was " + speedLevel);
				}

				await WriteHoldingRegistersAsync(325, BitConverter.GetBytes((uint)speedLevel));
			}
		} catch (Exception e)
		{
			_logger.LogError(e, "Failed to handle message to topic {topic} with payload: {payload}", evnt.ApplicationMessage.Topic, evnt.ApplicationMessage.ConvertPayloadToString());
		}
	}

	private async Task ReadStaticValuesAsync()
	{
		_logger.LogInformation("Reading static values");
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
				.Select(x => Convert.ToHexString(new byte[] { x }))));
		_result.Status.StartExploitation = Instant.FromUnixTimeSeconds(BitConverter.ToUInt32(await ReadHoldingRegistersAsync(669, 2)));
	}

	private async Task ReadDynamicValuesAsync()
	{
		_logger.LogInformation("Reading dynamic values");
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
		_result.Status.HALFan2Rpm = BitConverter.ToSingle(await ReadHoldingRegistersAsync(103, 2));
		_result.Status.FanSpeedLevel = BitConverter.ToUInt32(await ReadHoldingRegistersAsync(325, 2));

		if (_result.Status.SystemId.VOCSensor)
		{
			_result.Status.VolatileOrganicCompounds = BitConverter.ToUInt32(await ReadHoldingRegistersAsync(431, 2));
		}

		if(_result.Status.SystemId.RHSensor)
		{
			_result.Status.RelativeHumidity = BitConverter.ToUInt32(await ReadHoldingRegistersAsync(197, 2));
		}

		if(_result.Status.SystemId.Bypass)
		{
			_result.Status.BypassState = (DanthermUvcBypassState)BitConverter.ToUInt32(await ReadHoldingRegistersAsync(199, 2));
		}
	}

	public async Task PublishDiscoveryDocumentsAsync(DanthermKind data)
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
		var statusTopic = _topicHelper.GetStatusTopic(data.Status.SerialNum);

		var device = new MqttDiscoveryDevice
		{
			Name = deviceName,
			Identifiers = new List<string>
			{
				data.Status.SerialNum.ToString()
			}
		};

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Outdoor Temperature",
			UniqueId = $"dantherm_{data.Status.SerialNum}_outdoor_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ {GetJsonSelector(x => x.OutdoorTemperatureC)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Supply Temperature",
			UniqueId = $"dantherm_{data.Status.SerialNum}_supply_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ {GetJsonSelector(x => x.SupplyTemperatureC)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Extract Temperature",
			UniqueId = $"dantherm_{data.Status.SerialNum}_extract_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ {GetJsonSelector(x => x.ExtractTemperatureC)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Exhaust Temperature",
			UniqueId = $"dantherm_{data.Status.SerialNum}_exhaust_temp",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ {GetJsonSelector(x => x.ExhaustTemperatureC)} | round(1) }}}}",
			UnitOfMeasurement = HomeAssistantUnits.TEMP_CELSIUS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Fan1 Speed",
			UniqueId = $"dantherm_{data.Status.SerialNum}_fan1_rpm",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ {GetJsonSelector(x => x.HALFan1Rpm)} | round(0) }}}}",
			UnitOfMeasurement = "rpm"
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Fan2 Speed",
			UniqueId = $"dantherm_{data.Status.SerialNum}_fan2_rpm",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = $"{{{{ {GetJsonSelector(x => x.HALFan2Rpm)} | round(0) }}}}",
			UnitOfMeasurement = "rpm"
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Active Alarm",
			UniqueId = $"dantherm_{data.Status.SerialNum}_active_alarm",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = GetValueTemplate(x => x.LastActiveAlarm)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Work Hours",
			UniqueId = $"dantherm_{data.Status.SerialNum}_work_hours",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = GetValueTemplate(x => x.WorkTimeHours),
			UnitOfMeasurement = HomeAssistantUnits.TIME_HOURS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Remaining Filter Days",
			UniqueId = $"dantherm_{data.Status.SerialNum}_remaning_filter_days",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = GetValueTemplate(x => x.FilterRemaningTimeDays),
			UnitOfMeasurement = HomeAssistantUnits.TIME_DAYS.Value
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Current State",
			UniqueId = $"dantherm_{data.Status.SerialNum}_current_state",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = GetValueTemplate(x => x.CurrentBLState)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
		{
			Name = $"Bypass Active",
			UniqueId = $"dantherm_{data.Status.SerialNum}_bypass_state",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			ValueTemplate = GetValueTemplate(x => x.BypassState)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttSelectDiscoveryConfig()
		{
			Name = $"Fan Speed Level",
			UniqueId = $"dantherm_{data.Status.SerialNum}_fan_speed_level",
			Availability = availability,
			Device = device,
			StateTopic = statusTopic,
			CommandTopic = _topicHelper.GetSetFanSpeedLevel(data.Status.SerialNum),
			ValueTemplate = GetValueTemplate(x => x.FanSpeedLevel),
			Options = new List<string>
			{
				"0",
				"1",
				"2",
				"3",
				"4",
			}
		});

		await _mqtt.PublishDiscoveryDocument(new MqttButtonDiscoveryConfig()
		{
			Name = $"Start manual bypass",
			UniqueId = $"dantherm_{data.Status.SerialNum}_start_manual_bypass",
			Availability = availability,
			Device = device,
			CommandTopic = _topicHelper.GetSetUnitModeTopic(data.Status.SerialNum),
			CommandTemplate = Enum.GetName(DanthermUvcSetModeOfOperation.StartManualBypass)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttButtonDiscoveryConfig()
		{
			Name = $"End manual bypass",
			UniqueId = $"dantherm_{data.Status.SerialNum}_end_manual_bypass",
			Availability = availability,
			Device = device,
			CommandTopic = _topicHelper.GetSetUnitModeTopic(data.Status.SerialNum),
			CommandTemplate = Enum.GetName(DanthermUvcSetModeOfOperation.EndManualBypass)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttButtonDiscoveryConfig()
		{
			Name = $"Demand Mode",
			UniqueId = $"dantherm_{data.Status.SerialNum}_demand_mode",
			Availability = availability,
			Device = device,
			CommandTopic = _topicHelper.GetSetUnitModeTopic(data.Status.SerialNum),
			CommandTemplate = Enum.GetName(DanthermUvcSetModeOfOperation.Demand)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttButtonDiscoveryConfig()
		{
			Name = $"Manual Mode",
			UniqueId = $"dantherm_{data.Status.SerialNum}_manual_mode",
			Availability = availability,
			Device = device,
			CommandTopic = _topicHelper.GetSetUnitModeTopic(data.Status.SerialNum),
			CommandTemplate = Enum.GetName(DanthermUvcSetModeOfOperation.Manual)
		});

		await _mqtt.PublishDiscoveryDocument(new MqttButtonDiscoveryConfig()
		{
			Name = $"Week Program Mode",
			UniqueId = $"dantherm_{data.Status.SerialNum}_week_program_mode",
			Availability = availability,
			Device = device,
			CommandTopic = _topicHelper.GetSetUnitModeTopic(data.Status.SerialNum),
			CommandTemplate = Enum.GetName(DanthermUvcSetModeOfOperation.WeekProgram)
		});

		if (data.Status.SystemId.RHSensor)
		{
			await _mqtt.PublishDiscoveryDocument(new MqttSensorDiscoveryConfig()
			{
				Name = $"Relative Humidity",
				UniqueId = $"dantherm_{data.Status.SerialNum}_relative_humidity",
				Availability = availability,
				Device = device,
				StateTopic = statusTopic,
				ValueTemplate = GetValueTemplate(x => x.RelativeHumidity),
				UnitOfMeasurement = HomeAssistantUnits.PERCENTAGE.Value
			});
		}
	}

	private string GetJsonSelector<T>(Expression<Func<DanthermUvcStatus, T>> selector)
	{
		if (selector.Body is not MemberExpression member)
		{
			throw new ArgumentException(string.Format(
				"Expression '{0}' refers to a method, not a property.",
				selector.ToString()));
		}

		PropertyInfo? propInfo = member.Member as PropertyInfo;
		if (propInfo == null)
		{
			throw new ArgumentException(string.Format(
				"Expression '{0}' refers to a field, not a property.",
				selector.ToString()));
		}

		var converter = _jsonContext.Options.PropertyNamingPolicy!;
		var statusKey = converter.ConvertName(nameof(DanthermKind.Status));
		return $"value_json.{statusKey}.{converter.ConvertName(propInfo.Name)}";
	}

	private string GetValueTemplate<T>(Expression<Func<DanthermUvcStatus, T>> selector)
	{
		return $"{{{{ {GetJsonSelector(selector)} }}}}";
	}
}
