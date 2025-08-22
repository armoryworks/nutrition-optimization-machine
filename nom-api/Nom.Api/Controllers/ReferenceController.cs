// File: Nom.Api/Controllers/ReferenceController.cs

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using System;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class ReferenceController : BaseApiController
    {
        private readonly ILogger<ReferenceController> _logger;
        private readonly IReferenceOrchestrationService _referenceOrch;

        public ReferenceController(ILogger<ReferenceController> logger, IReferenceOrchestrationService referenceOrch)
        {
            _logger = logger;
            _referenceOrch = referenceOrch;
        }


    }
}