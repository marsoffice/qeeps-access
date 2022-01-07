using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using FluentValidation;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;

[assembly: FunctionsStartup(typeof(MarsOffice.Qeeps.Access.Startup))]
namespace MarsOffice.Qeeps.Access
{
    public class Startup : FunctionsStartup
    {
        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            FunctionsHostBuilderContext context = builder.GetContext();
            var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
            builder.ConfigurationBuilder
                .AddJsonFile(Path.Combine(context.ApplicationRootPath, "appsettings.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Combine(context.ApplicationRootPath, $"appsettings.{env}.json"), optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();
        }

        private static GraphServiceClient BuildGraphClient()
        {
            TokenCredential tokenCredential = null;
            var envVar = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT");
            var isDevelopmentEnvironment = string.IsNullOrEmpty(envVar) || envVar.ToLower() == "development";

            if (isDevelopmentEnvironment)
            {
                tokenCredential = new AzureCliCredential();
            }
            else
            {
                tokenCredential = new DefaultAzureCredential();
            }

            var accessToken = tokenCredential.GetToken(
                new TokenRequestContext(scopes: new string[] { "https://graph.microsoft.com/.default" }),
                cancellationToken: System.Threading.CancellationToken.None
            );
            var graphServiceClient = new GraphServiceClient(
                new DelegateAuthenticationProvider((requestMessage) =>
            {
                requestMessage
                    .Headers
                    .Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
                return Task.CompletedTask;
            }));
            return graphServiceClient;
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = builder.GetContext().Configuration;
            builder.Services.AddAutoMapper(typeof(Startup).Assembly);
            builder.Services.AddValidatorsFromAssembly(typeof(Startup).Assembly);
            builder.Services.AddTransient(_ => BuildGraphClient());
        }
    }
}