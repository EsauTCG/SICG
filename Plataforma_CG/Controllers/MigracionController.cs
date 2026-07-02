using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Services;


namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MigracionController : ControllerBase
    {
        private readonly ActiveDirectoryService _adService;
        private readonly DatabaseService _dbService;

        public MigracionController(ActiveDirectoryService adService, DatabaseService dbService)
        {
            _adService = adService;
            _dbService = dbService;
        }

        [HttpPost("migrar")]
        public async Task<IActionResult> MigrarUsuarios()
        {
            try
            {
                var usuarios = _adService.ObtenerUsuarios();

                if (usuarios.Count == 0)
                    return NotFound("No se encontraron usuarios en Active Directory.");

                await _dbService.GuardarUsuariosAsync(usuarios);

                return Ok(new
                {
                    Mensaje = "Migración completada correctamente.",
                    TotalUsuarios = usuarios.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}
