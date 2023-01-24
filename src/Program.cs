using Prometheus;
using Serilog;
using ToMqttNet;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;


builder.Host.UseSerilog((options, loggerConf) =>
{
	loggerConf
		.MinimumLevel.Debug()
		.Enrich.FromLogContext()
		.Enrich.WithThreadId()
		.WriteTo.Console(outputTemplate: "[{Timestamp:yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz} {Level:u3} {ThreadId} {SourceContext}] {Message:lj}{NewLine}{Exception}")
		.ReadFrom.Configuration(options.Configuration);
});

services.AddHealthChecks();
services.AddHostedService<DanthermModBusHandler>();
services.AddMqttConnection(options =>
{
	options.NodeId = "dantherm";
	options.ClientId = "danthermtomqtt";
});


services.AddSingleton<CollectorRegistry>(x =>
{
	var registry = Metrics.NewCustomRegistry();

	DotNetStats.Register(registry);

	return registry;
});

services.AddSingleton<MetricFactory>(x =>
{
	var factory = Metrics.WithCustomRegistry(x.GetRequiredService<CollectorRegistry>());

	return factory;
});

services.AddSingleton<DanthermToMqttMetrics>();
services.AddOptions<DanthermUvcSpec>()
	.BindConfiguration(nameof(DanthermUvcSpec));

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMetrics(registry: app.Services.GetRequiredService<CollectorRegistry>());

app.Run();
