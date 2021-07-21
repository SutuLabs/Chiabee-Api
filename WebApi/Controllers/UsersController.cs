namespace WebApi.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Authorization;
    using WebApi.Services;
    using System.Threading.Tasks;
    using WebApi.Models;
    using WebApi.Entities;
    using WebApi.Helpers;

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class UsersController : ControllerBase
    {
        private IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [AllowAnonymous]
        [HttpPost("authenticate")]
        public async Task<IActionResult> Authenticate([FromBody] AuthenticateModel model)
        {
            var user = await _userService.Authenticate(model.Username, model.Password);

            if (user == null)
                return BadRequest(new { message = "Username or password is incorrect" });

            return Ok(user);
        }

        [HttpPost("users")]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> CreateUser(string username, string password, UserRole role, string firstName, string lastName)
        {
            await _userService.CreateUser(username, password.Sha256(), role, firstName, lastName);
            return Ok();
        }

        [HttpGet]
        [Authorize(nameof(UserRole.Admin))]
        public async Task<IActionResult> GetAll()
        {
            var users = await _userService.GetAll();
            return Ok(users);
        }
    }
}
