
using NodaTime;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
namespace Dantherm2Mqtt;

public class DanthermToMqttMetrics
{
	private readonly ConcurrentDictionary<ulong, (Instant When, DanthermKind Update)> _lastUpdates = new();
	private readonly Meter _meter;

	public DanthermToMqttMetrics(IMeterFactory metricsFactory)
	{
		_meter = metricsFactory.Create("DanthermToMqtt");
	}

	public void UpdateMetrics(DanthermKind kind)
	{
		_lastUpdates.AddOrUpdate(kind.Status.SerialNum,
		(serial) => {
				var tagList = new TagList()
				{
					{"device_serial", serial }
				};

				_meter.CreateObservableCounter(
					"danthermtomqtt_last_active_alarm",
					() => (int)_lastUpdates[serial].Update.Status.LastActiveAlarm,
					unit: null,
					description: "The last active alarm, zero = none, see Dantherm documentation if not zero",
					tagList);

				_meter.CreateObservableGauge(
					"danthermtomqtt_last_data_pull_timestamp_seconds",
					() => _lastUpdates[serial].When.ToUnixTimeSeconds(),
					unit: "seconds",
					description: "The last time data was pulled successfully",
					tagList);

				return (SystemClock.Instance.GetCurrentInstant(), kind);
			},
			(serial, current) => (SystemClock.Instance.GetCurrentInstant(), kind));
	}
}
