using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class UserPreferences
    {
        public UserPreferences()
        {
        }

        [FunctionName("UserPreferences")]
        public async Task<IActionResult> GetUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/userPreferences")] HttpRequest req)
        {
            var userId = QeepsPrincipal.Parse(req).FindFirst("id").Value;
            return null;  
        }
    }
}
