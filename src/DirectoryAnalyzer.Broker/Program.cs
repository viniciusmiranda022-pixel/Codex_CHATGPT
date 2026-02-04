using System.Security.Authentication;
using DirectoryAnalyzer.Broker.Configuration;
using DirectoryAnalyzer.Broker.Hubs;
using DirectoryAnalyzer.Broker.Services;
using DirectoryAnalyzer.Broker.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var brokerSettings = builder.Configuration.GetSection("Broker").Get<BrokerSettings>() ?? new BrokerSettings();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        httpsOptions.ClientCertificateMode = brokerSettings.EnableClientCertificateValidation
            ? ClientCertificateMode.RequireCertificate
            : ClientCertificateMode.AllowCertificate;
    });
});

builder.Services.AddSingleton(brokerSettings);
builder.Services.AddSingleton<IJobStore, InMemoryJobStore>();
builder.Services.AddSingleton<IResultStore, InMemoryResultStore>();
builder.Services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
builder.Services.AddSingleton(new ClientCertificateValidator(brokerSettings.AllowedThumbprints, brokerSettings.TrustedCaThumbprints));

builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseHttpsRedirection();

if (brokerSettings.EnableClientCertificateValidation)
{
    app.Use(async (context, next) =>
    {
        var cert = await context.Connection.GetClientCertificateAsync();
        var validator = context.RequestServices.GetRequiredService<ClientCertificateValidator>();
        if (cert == null || !validator.Validate(cert))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Certificado de cliente inválido.");
            return;
        }

        await next();
    });
}

app.MapControllers();
app.MapHub<AgentHub>("/agent-hub");

app.Run();
