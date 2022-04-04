using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Xels.Bitcoin.Features.Api
{
    /// <summary>
    /// Configures the Swagger generation options.
    /// </summary>
    /// <remarks>This allows API versioning to define a Swagger document per API version after the
    /// <see cref="IApiVersionDescriptionProvider"/> service has been resolved from the service container.
    /// Adapted from https://github.com/microsoft/aspnet-api-versioning/blob/master/samples/aspnetcore/SwaggerSample/ConfigureSwaggerOptions.cs.
    /// </remarks>
    public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
    {
        private static readonly string[] ApiXmlDocuments = new string[]
        {
            "Xels.Bitcoin.xml",
            "Xels.Bitcoin.Features.BlockStore.xml",
            "Xels.Bitcoin.Features.ColdStaking.xml",
            "Xels.Bitcoin.Features.Consensus.xml",
            "Xels.Bitcoin.Features.PoA.xml",
            "Xels.Bitcoin.Features.MemoryPool.xml",
            "Xels.Bitcoin.Features.Miner.xml",
            "Xels.Bitcoin.Features.Notifications.xml",
            "Xels.Bitcoin.Features.RPC.xml",
            "Xels.Bitcoin.Features.SignalR.xml",
            "Xels.Bitcoin.Features.SmartContracts.xml",
            "Xels.Bitcoin.Features.Wallet.xml",
            "Xels.Bitcoin.Features.WatchOnlyWallet.xml",
            "Xels.Features.Diagnostic.xml",
            "Xels.Features.FederatedPeg.xml"
        };

        private readonly IApiVersionDescriptionProvider provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureSwaggerOptions"/> class.
        /// </summary>
        /// <param name="provider">The <see cref="IApiVersionDescriptionProvider">provider</see> used to generate Swagger documents.</param>
        public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
        {
            this.provider = provider;
        }

        /// <inheritdoc />
        public void Configure(SwaggerGenOptions options)
        {
            // Add a swagger document for each discovered API version
            foreach (ApiVersionDescription description in this.provider.ApiVersionDescriptions)
            {
                options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
            }

            // Includes XML comments in Swagger documentation 
            string basePath = AppContext.BaseDirectory;
            foreach (string xmlPath in ApiXmlDocuments.Select(xmlDocument => Path.Combine(basePath, xmlDocument)))
            {
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath);
                }
            }
        }

        static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
        {
            var info = new OpenApiInfo
            {
                Title = "Xels Node API",
                Version = description.ApiVersion.ToString(),
                Description = "Access to the Xels Node's api."
            };

            if (info.Version.Contains("dev"))
            {
                info.Description += " This version of the API is in development and subject to change. Use an earlier version for production applications.";
            }

            if (description.IsDeprecated)
            {
                info.Description += " This API version has been deprecated.";
            }

            return info;
        }
    }
}
