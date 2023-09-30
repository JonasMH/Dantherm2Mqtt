using NodaTime.Text;
using NodaTime;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Dantherm2Mqtt;

[JsonSerializable(typeof(DanthermKind))]
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	WriteIndented = true,
	Converters = new[] { typeof(NodaTimeInstantConverter) })]
public partial class DanthermMqttJsonContext : JsonSerializerContext
{

}

public class NodaTimeInstantConverter : JsonConverter<Instant>
{
	private InstantPattern _pattern = InstantPattern.ExtendedIso;

	public override Instant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var text = reader.GetString();
		return _pattern.Parse(text!).Value;
	}

	public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(_pattern.Format(value));
	}
}
