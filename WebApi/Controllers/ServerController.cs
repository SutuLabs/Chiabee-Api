namespace WebApi.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Authorization;
    using WebApi.Services;
    using System.Threading.Tasks;
    using WebApi.Models;
    using System.Linq;

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

        [HttpGet("servers")]
        public async Task<IActionResult> GetServersInfo()
        {
            var info = await serverService.GetServersInfo();
            return Ok(info);
        }

        [HttpGet("plotter")]
        public async Task<IActionResult> GetPlotterInfo()
        {
            var info = await serverService.GetPlotterInfo();
            return Ok(info);
        }

        [HttpGet("farmer")]
        public async Task<IActionResult> GetFarmerInfo()
        {
            var info = await serverService.GetFarmerInfo();
            return Ok(info);
        }

        [HttpGet("errors")]
        public async Task<IActionResult> GetFarmerErrorInfo()
        {
            var info = serverService.errorList.Select(_ => _.Value).ToArray();
            return Ok(info);
        }

        [HttpGet("events")]
        public async Task<IActionResult> GetFarmerEventInfo()
        {
            var info = serverService.eventList.ToArray();
            return Ok(info);
        }
    }
}
