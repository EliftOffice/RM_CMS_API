using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Security;
using Microsoft.AspNetCore.Authorization;

namespace RM_CMS.Controllers.Admin
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = Policies.AdminOnly)]
    public class SiteAdminController : ControllerBase
    {
    }
}
