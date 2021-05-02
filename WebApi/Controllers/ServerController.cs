namespace WebApi.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Authorization;
    using WebApi.Services;
    using System.Threading.Tasks;
    using WebApi.Models;

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ServerController : ControllerBase
    {
        private ServerService serverService;

        public ServerController(ServerService serverService)
        {
            this.serverService = serverService;
        }

        [HttpGet("info")]
        public async Task<IActionResult> GetInfo()
        {
            var info = await serverService.GetPlotterInfo();
            var info2 = await serverService.GetFarmInfo();
            return Ok(new { plot = info, farm = info2 });
        }
    }
}
