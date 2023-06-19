using ToMqttNet;

public class DanthermTopicHelper
{
	private readonly IMqttConnectionService _mqtt;

	public DanthermTopicHelper(IMqttConnectionService mqtt)
	{
		_mqtt = mqtt;
	}

	public string GetStatusTopic(ulong serialNum)
	{
		return $"{_mqtt.MqttOptions.NodeId}/status/{serialNum}";
	}


	public string GetSetTopicRegex(ulong serialNum)
	{
		return $"{_mqtt.MqttOptions.NodeId}/write/{serialNum}/+";
	}

	public string GetSetUnitModeTopic(ulong serialNum)
	{
		return $"{_mqtt.MqttOptions.NodeId}/write/{serialNum}/activeMode";
	}

	public string GetSetFanSpeedLevel(ulong serialNum)
	{
		return $"{_mqtt.MqttOptions.NodeId}/write/{serialNum}/fanSpeedLevel";
	}
}
