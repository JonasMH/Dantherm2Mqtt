using ToMqttNet;
namespace Dantherm2Mqtt;

public class DanthermTopicHelper(IMqttConnectionService mqtt)
{
	public string GetStatusTopic(ulong serialNum)
	{
		return $"{mqtt.MqttOptions.NodeId}/status/{serialNum}";
	}

	public string GetSetTopicRegex(ulong serialNum)
	{
		return $"{mqtt.MqttOptions.NodeId}/write/{serialNum}/+";
	}

	public string GetSetUnitModeTopic(ulong serialNum)
	{
		return $"{mqtt.MqttOptions.NodeId}/write/{serialNum}/activeMode";
	}

	public string GetSetFanSpeedLevel(ulong serialNum)
	{
		return $"{mqtt.MqttOptions.NodeId}/write/{serialNum}/fanSpeedLevel";
	}
}
