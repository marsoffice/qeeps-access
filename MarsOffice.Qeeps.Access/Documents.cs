using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{
    public class Documents
    {
        private readonly IMapper _mapper;
        private readonly IConfiguration _config;

        public Documents(IMapper mapper, IConfiguration config)
        {
            _config = config;
            _mapper = mapper;
        }

        [FunctionName("GetDocument")]
        public async Task<IActionResult> GetDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/documents/{id}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
            {
                client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");
#if DEBUG
                var db = new Database
                {
                    Id = "access"
                };
                await client.CreateDatabaseIfNotExistsAsync(db);

                var col = new DocumentCollection
                {
                    Id = "Documents",
                    PartitionKey = new PartitionKeyDefinition
                    {
                        Version = PartitionKeyDefinitionVersion.V2,
                        Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                    }
                };
                await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif
                var id = req.RouteValues["id"].ToString();
                var docId = UriFactory.CreateDocumentUri("access", "Documents", id);

                DocumentEntity foundDocumentResponse = null;
                try
                {
                    foundDocumentResponse = (await client.ReadDocumentAsync<DocumentEntity>(docId, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("DocumentEntity")
                    }))?.Document;
                }
                catch (Exception) { }
                if (foundDocumentResponse == null)
                {
                    return new JsonResult(null);
                }
                return new JsonResult(_mapper.Map<DocumentDto>(foundDocumentResponse), new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("SaveDocument")]
        public async Task<IActionResult> SaveDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/documents")] HttpRequest req,
            [CosmosDB(
                ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
            {
                client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");
#if DEBUG
                var db = new Database
                {
                    Id = "access"
                };
                await client.CreateDatabaseIfNotExistsAsync(db);

                var col = new DocumentCollection
                {
                    Id = "Documents",
                    PartitionKey = new PartitionKeyDefinition
                    {
                        Version = PartitionKeyDefinitionVersion.V2,
                        Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                    }
                };
                await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif

                var principal = QeepsPrincipal.Parse(req);
                if (!principal.FindAll(x => x.Type == "roles").Any(x => x.Value == "Owner"))
                {
                    return new StatusCodeResult(401);
                }

                var json = string.Empty;
                using (var streamReader = new StreamReader(req.Body))
                {
                    json = await streamReader.ReadToEndAsync();
                }
                var payload = JsonConvert.DeserializeObject<DocumentDto>(json, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });


                var docId = UriFactory.CreateDocumentUri("access", "Documents", payload.Id);

                var docEntity = _mapper.Map<DocumentEntity>(payload);
                var collection = UriFactory.CreateDocumentCollectionUri("access", "Documents");
                await client.UpsertDocumentAsync(collection, docEntity, new RequestOptions
                {
                    PartitionKey = new PartitionKey("DocumentEntity")
                }, true);

                return new OkResult();
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }
    }
}
