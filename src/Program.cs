using MQTTnet.Client;
using Prometheus;
using Serilog;
using System.Security.Cryptography.X509Certificates;
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
services.AddSingleton<DanthermTopicHelper>();
services.AddMqttConnection(options =>
{
	options.NodeId = "dantherm";

    var mqttConf = builder.Configuration.GetSection("MqttConnectionOptions");
    var tcpOptions = new MqttClientTcpOptions
    {
        Server = mqttConf["Server"],
        Port = mqttConf.GetSection("Port")?.Get<int>(),
    };

    var useTls = mqttConf.GetSection("UseTls")?.Get<bool>() ?? false;

    if (useTls)
    {
        var caCrt = new X509Certificate2(mqttConf["CaCrt"]);
        var clientCrt = X509Certificate2.CreateFromPemFile(mqttConf["ClientCrt"], mqttConf["ClientKey"]);


        tcpOptions.TlsOptions = new MqttClientTlsOptions
        {
            UseTls = true,
            SslProtocol = System.Security.Authentication.SslProtocols.Tls12,
            Certificates = new List<X509Certificate>()
            {
                clientCrt, caCrt
            },
            CertificateValidationHandler = (certContext) =>
            {
                X509Chain chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.VerificationTime = DateTime.Now;
                chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 0);
                chain.ChainPolicy.CustomTrustStore.Add(caCrt);
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

                // convert provided X509Certificate to X509Certificate2
                var x5092 = new X509Certificate2(certContext.Certificate);

                return chain.Build(x5092);
            }
        };
    }


    options.ClientOptions.ChannelOptions = tcpOptions;
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

services.AddSingleton<IDanthermToMqttMetrics, DanthermToMqttMetrics>();
services.AddSingleton<IModbusClient, ModbusClientBinding>();
services.AddOptions<DanthermUvcSpec>()
	.BindConfiguration(nameof(DanthermUvcSpec));

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMetrics(registry: app.Services.GetRequiredService<CollectorRegistry>());

app.Run();
