using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfiguration _config;

        public ConfigController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("password")]
        public IActionResult GetPassword()
        {
            var password = _config["Config:Password"];
            return Ok(new { password });
        }
    }
}
