using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImpresoraController : ControllerBase
    {
        [HttpPost("imprimir")]
        public async Task<IActionResult> Imprimir([FromBody] string zpl, [FromQuery] string ip)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var url = $"http://{ip}/pstprnt";
            var content = new StringContent(zpl, Encoding.UTF8, "text/plain");

            try
            {
                var response = await client.PostAsync(url, content);
                return Ok(new { success = response.IsSuccessStatusCode });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }

}
