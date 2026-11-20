using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Plataforma_CG.Controllers
{
    /// <summary>
    /// Foto de perfil por usuario.
    /// - La imagen se guarda en App_Data/avatars (fuera de wwwroot: no se puede abrir por URL directa).
    /// - El nombre del archivo es un hash del usuario (no se puede adivinar ni usar para leer otras rutas).
    /// - Cada usuario solo puede ver, cambiar o quitar SU propia foto.
    /// - El navegador ya la envía recortada a 256 px (WebP o JPG); aquí se valida tamaño y tipo real.
    /// </summary>
    [Authorize]
    public class PerfilController : Controller
    {
        private const long MaxBytes = 512 * 1024; // 512 KB (una foto de 256 px pesa ~20–40 KB)
        private readonly IWebHostEnvironment _env;

        public PerfilController(IWebHostEnvironment env)
        {
            _env = env;
        }

        // GET /Perfil/Avatar — foto del usuario actual (404 si no tiene; el layout muestra sus iniciales).
        [HttpGet]
        public IActionResult Avatar()
        {
            var ruta = RutaAvatar();
            if (ruta == null || !System.IO.File.Exists(ruta)) return NotFound();

            var tipo = DetectarTipo(LeerCabecera(ruta));
            if (tipo == null) return NotFound();

            var info = new FileInfo(ruta);
            var etag = new EntityTagHeaderValue($"\"{info.LastWriteTimeUtc.Ticks:x}\"");

            // El navegador la guarda, pero pregunta si cambió (respuesta 304 si sigue igual).
            Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
            return PhysicalFile(ruta, tipo, info.LastWriteTimeUtc, etag);
        }

        // POST /Perfil/SubirAvatar — campo "archivo".
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxBytes + 16 * 1024)]
        public async Task<IActionResult> SubirAvatar(IFormFile archivo)
        {
            var ruta = RutaAvatar();
            if (ruta == null) return Unauthorized(new { ok = false, msg = "Sesión no válida." });

            if (archivo == null || archivo.Length == 0)
                return BadRequest(new { ok = false, msg = "No se recibió ninguna imagen." });

            if (archivo.Length > MaxBytes)
                return BadRequest(new { ok = false, msg = "La imagen es demasiado pesada (máximo 512 KB)." });

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await archivo.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            // Se valida el contenido real del archivo, no solo la extensión.
            if (DetectarTipo(bytes) == null)
                return BadRequest(new { ok = false, msg = "El archivo no es una imagen JPG, PNG o WebP válida." });

            // Escritura atómica: primero a un temporal y luego se reemplaza.
            var temporal = ruta + ".tmp";
            await System.IO.File.WriteAllBytesAsync(temporal, bytes);
            System.IO.File.Move(temporal, ruta, overwrite: true);

            return Json(new { ok = true, msg = "Foto actualizada." });
        }

        // POST /Perfil/QuitarAvatar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult QuitarAvatar()
        {
            var ruta = RutaAvatar();
            if (ruta == null) return Unauthorized(new { ok = false, msg = "Sesión no válida." });

            if (System.IO.File.Exists(ruta)) System.IO.File.Delete(ruta);
            return Json(new { ok = true, msg = "Foto quitada." });
        }

        // ------------------------------------------------------------------

        private string? RutaAvatar()
        {
            // Si el sistema tiene un claim de Id de usuario, conviene usarlo aquí en lugar del nombre.
            var usuario = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuario)) return null;

            var carpeta = Path.Combine(_env.ContentRootPath, "App_Data", "avatars");
            Directory.CreateDirectory(carpeta);

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(usuario.Trim().ToLowerInvariant())));
            return Path.Combine(carpeta, hash + ".img");
        }

        private static byte[] LeerCabecera(string ruta)
        {
            var buffer = new byte[12];
            using var fs = System.IO.File.OpenRead(ruta);
            var leidos = fs.Read(buffer, 0, buffer.Length);
            return leidos == buffer.Length ? buffer : Array.Empty<byte>();
        }

        private static string? DetectarTipo(byte[] b)
        {
            if (b == null || b.Length < 12) return null;
            // JPEG: FF D8 FF
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            // WebP: "RIFF" .... "WEBP"
            if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
                b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "image/webp";
            return null;
        }
    }
}
