namespace WebApi.Controllers
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using WebApi.Entities;
    using WebApi.Models;
    using WebApi.Services;

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class MiscController : ControllerBase
    {
        private readonly ILogger<MiscController> logger;
        private readonly PersistentService persistentService;
        private readonly IMemoryCache memoryCache;
        private readonly AppSettings appSettings;

        public MiscController(
            ILogger<MiscController> logger,
            PersistentService persistentService,
            IMemoryCache memoryCache,
            IOptions<AppSettings> appSettings)
        {
            this.logger = logger;
            this.persistentService = persistentService;
            this.memoryCache = memoryCache;
            this.appSettings = appSettings.Value;
        }

        [HttpGet("prices")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPrice()
        {
            var market = await this.persistentService.RetrieveEntityAsync<PriceStateEntity>();
            var prices = JsonConvert.DeserializeObject<PriceEntity[]>(market.PricesJson);
            return this.Ok(prices);
        }
    }
}