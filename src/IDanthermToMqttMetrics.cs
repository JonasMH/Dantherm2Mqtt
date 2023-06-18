public interface IDanthermToMqttMetrics
{
	void SetLastDataPull(DanthermKind kind, bool succeeded);
	void UpdateMetrics(DanthermKind kind);
}