using Dantherm2Mqtt;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
using OpenTelemetry.Metrics;
using System.Security.Cryptography.X509Certificates;
using ToMqttNet;

var builder = WebApplication.CreateSlimBuilder(args);
var services = builder.Services;

builder.Logging.AddSimpleConsole(options =>
{
	options.IncludeScopes = true;
	options.SingleLine = true;
	options.TimestampFormat = "HH:mm:ss ";
});

services.AddHealthChecks();
services.AddHostedService<DanthermModBusHandler>();
services.AddSingleton<DanthermTopicHelper>();
builder.Services.AddMqttConnection();

services.AddOpenTelemetry()
	.WithMetrics(builder =>
	{
		builder.AddPrometheusExporter();
		builder.AddMeter("DanthermToMqtt");
		builder.AddMeter("Microsoft.AspNetCore.Hosting",
						 "Microsoft.AspNetCore.Server.Kestrel");
	});

services.AddSingleton<DanthermToMqttMetrics>();
services.AddSingleton<IModbusClient, ModbusClientBinding>();
services.AddOptions<DanthermUvcSpec>().BindConfiguration(nameof(DanthermUvcSpec));

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();

public class MqttOptions
{
	public int Port { get; set; }
	public bool UseTls { get; set; }
	public string Server { get; set; } = null!;
	public string CaCrt { get; set; } = null!;
	public string ClientCrt { get; set; } = null!;
	public string ClientKey { get; set; } = null!;
}
