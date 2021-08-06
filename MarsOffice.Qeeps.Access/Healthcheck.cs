using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class Healthcheck
    {
        private readonly ConnectionMultiplexer _mux;
        private readonly IDatabase _redisDb;

        public Healthcheck(ConnectionMultiplexer mux)
        {
            _mux = mux;
            _redisDb = mux.GetDatabase();
        }

        [FunctionName("Healthcheck")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "healthcheck")] HttpRequest req)
        {
            if (!_mux.IsConnected)
            {
                return new StatusCodeResult(500);
            }
            var isRedisEmpty = !await _redisDb.KeyExistsAsync("dummy");
            if (isRedisEmpty)
            {
                return new StatusCodeResult(500);
            }
            return new OkResult();
        }
    }
}
