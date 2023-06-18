using Prometheus;

public class DanthermToMqttMetrics : IDanthermToMqttMetrics
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
