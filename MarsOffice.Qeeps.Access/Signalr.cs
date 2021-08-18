using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;

namespace MarsOffice.Qeeps.Access
{
    public class Signalr
    {
        public Signalr()
        {
        }

        [FunctionName("SignalrNegotiate")]
        public async Task<SignalRConnectionInfo> SignalrNegotiate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "api/access/signalr/negotiate")] HttpRequest req,
            IBinder binder
            )
        {
            var principal = QeepsPrincipal.Parse(req);
            var connectionInfo = await binder.BindAsync<SignalRConnectionInfo>(new SignalRConnectionInfoAttribute
            {
                HubName = "main",
                UserId = principal.FindFirstValue("id"),
                ConnectionStringSetting = "signalrconnectionstring"
            });
            return connectionInfo;
        }
    }
}
