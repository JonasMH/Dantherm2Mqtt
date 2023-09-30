using Dantherm2Mqtt;
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
        var caCrt = new X509Certificate2(mqttConf["CaCrt"]!);
        var clientCrt = X509Certificate2.CreateFromPemFile(mqttConf["ClientCrt"]!, mqttConf["ClientKey"]);


        tcpOptions.TlsOptions = new MqttClientTlsOptions
        {
            UseTls = true,
            SslProtocol = System.Security.Authentication.SslProtocols.Tls12,
			ClientCertificatesProvider = new DefaultMqttCertificatesProvider(new List<X509Certificate>()
			{
				clientCrt, caCrt
			}),
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
