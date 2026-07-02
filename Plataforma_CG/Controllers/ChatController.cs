using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models.Chat;   // <-- ajusta al namespace donde pusiste las clases

namespace Plataforma_CG.Controllers
{


    [Authorize]
    [Route("[controller]")]
    public class ChatController : Controller
    {
        private readonly AppDbContext _db;

        public ChatController(AppDbContext db)
        {
            _db = db;
        }

        // ================== UTIL ==================
        private string GetUsuarioIdActual()
        {
            // 1) Correos en claims (para AD, Azure, local)
            var email = User?.Claims?
                .FirstOrDefault(c =>
                    c.Type.Contains("email") ||
                    c.Type.Contains("upn") ||
                    c.Type.Contains("nameidentifier")
                )?.Value;

            if (!string.IsNullOrWhiteSpace(email))
                return email.ToLower().Trim();

            // 2) User.Identity.Name (último recurso)
            return (User?.Identity?.Name ?? "anonimo").ToLower().Trim();
        }

        // ================== 1. LISTAR ÁREAS ==================
        // GET /Chat/Areas
        [HttpGet("Areas")]
        public async Task<IActionResult> GetAreas()
        {
            var areas = await _db.ChatAreas
                .Where(a => a.Activo)
                .OrderBy(a => a.Nombre)
                .Select(a => new
                {
                    a.IdArea,
                    a.Nombre,
                    a.ResponsableUsuarioId
                })
                .ToListAsync();

            return Json(areas);
        }

        // ================== 2. CREAR / RECUPERAR CONVERSACIÓN ==================
        // POST /Chat/NuevaConversacion   (body: { idArea: 1 })
        [HttpPost("NuevaConversacion")]
        public async Task<IActionResult> NuevaConversacion([FromBody] NuevaConversacionDto dto)
        {
            if (dto == null || dto.IdArea <= 0)
                return BadRequest(new { ok = false, mensaje = "Área inválida." });

            var usuarioId = GetUsuarioIdActual();
            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized(new { ok = false, mensaje = "Sesión no válida." });

            var area = await _db.ChatAreas
                .FirstOrDefaultAsync(a => a.IdArea == dto.IdArea && a.Activo);

            if (area == null)
                return BadRequest(new { ok = false, mensaje = "El área no existe o está inactiva." });

            // 👇 IMPORTANTE: reusar conversación abierta
            var conv = await _db.ChatConversaciones
                .FirstOrDefaultAsync(c =>
                    c.IdArea == dto.IdArea &&
                    c.UsuarioId == usuarioId &&
                    !c.Cerrada);

            if (conv == null)
            {
                conv = new ChatConversacion
                {
                    IdArea = dto.IdArea,
                    UsuarioId = usuarioId,
                    FechaInicio = DateTime.Now,
                    Cerrada = false
                };

                _db.ChatConversaciones.Add(conv);
                await _db.SaveChangesAsync();
            }

            return Json(new
            {
                ok = true,
                idConversacion = conv.IdConversacion,
                area = area.Nombre
            });
        }


        // ================== 3. ENVIAR MENSAJE ==================
        // POST /Chat/Enviar   (body: { idConversacion: 1, texto: "Hola" })
        [HttpPost("Enviar")]
        public async Task<IActionResult> Enviar([FromBody] EnviarMensajeDto dto)
        {
            if (dto == null || dto.IdConversacion <= 0 || string.IsNullOrWhiteSpace(dto.Texto))
                return BadRequest(new { ok = false, mensaje = "Datos incompletos." });

            var usuarioId = GetUsuarioIdActual();
            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized(new { ok = false, mensaje = "Sesión no válida." });

            var conv = await _db.ChatConversaciones
                .Include(c => c.Area)
                .FirstOrDefaultAsync(c => c.IdConversacion == dto.IdConversacion);

            if (conv == null)
                return NotFound(new { ok = false, mensaje = "La conversación no existe." });

            if (conv.Cerrada)
                return BadRequest(new { ok = false, mensaje = "La conversación está cerrada." });

            // (opcional) Seguridad simple:
            // Cliente: dueño de la conversación
            // Responsable: ResponsableUsuarioId del área
            var responsable = conv.Area?.ResponsableUsuarioId;
            var esCliente = conv.UsuarioId.Equals(usuarioId, StringComparison.OrdinalIgnoreCase);
            var esResponsable = !string.IsNullOrEmpty(responsable) &&
                                responsable.Equals(usuarioId, StringComparison.OrdinalIgnoreCase);

            if (!esCliente && !esResponsable)
                return Forbid("No tienes permiso para enviar mensajes en esta conversación.");

            var msg = new ChatMensaje
            {
                IdConversacion = conv.IdConversacion,
                AutorUsuarioId = usuarioId,
                Texto = dto.Texto.Trim(),
                Fecha = DateTime.Now,
                Leido = false
            };

            _db.ChatMensajes.Add(msg);
            await _db.SaveChangesAsync();

            return Json(new
            {
                ok = true,
                idMensaje = msg.IdMensaje,
                fecha = msg.Fecha
            });
        }

        // ================== 4. OBTENER MENSAJES DE UNA CONVERSACIÓN ==================
        // GET /Chat/Mensajes/5
        [HttpGet("Mensajes/{id:int}")]
        public async Task<IActionResult> Mensajes(int id)
        {
            var usuarioId = GetUsuarioIdActual();
            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized(new { ok = false });

            var conv = await _db.ChatConversaciones
                .Include(c => c.Area)
                .FirstOrDefaultAsync(c => c.IdConversacion == id);

            if (conv == null)
                return NotFound(new { ok = false, mensaje = "Conversación no encontrada" });

            var mensajes = await _db.ChatMensajes
                .Where(m => m.IdConversacion == id)
                .OrderBy(m => m.Fecha)
                .ToListAsync();

            // 👇 Marcar como leídos todos los que no sean míos
            var noLeidos = mensajes
                .Where(m => !m.Leido && m.AutorUsuarioId != usuarioId)
                .ToList();

            if (noLeidos.Count > 0)
            {
                foreach (var m in noLeidos)
                    m.Leido = true;

                await _db.SaveChangesAsync();
            }

            var result = mensajes.Select(m => new
            {
                idMensaje = m.IdMensaje,
                texto = m.Texto,
                fecha = m.Fecha,
                esMio = m.AutorUsuarioId == usuarioId
            });

            return Json(new { ok = true, mensajes = result });
        }




        // ================== 5. MIS CONVERSACIONES COMO CLIENTE ==================
        // GET /Chat/MisConversacionesCliente
        [HttpGet("MisConversacionesCliente")]
        public async Task<IActionResult> MisConversacionesCliente()
        {
            var usuarioId = GetUsuarioIdActual();
            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized();

            var lista = await _db.ChatConversaciones
                .Include(c => c.Area)
                .Where(c => c.UsuarioId == usuarioId)
                .OrderByDescending(c => c.FechaInicio)
                .Select(c => new ConversacionResumenViewModel
                {
                    IdConversacion = c.IdConversacion,
                    UsuarioId = c.UsuarioId,
                    NombreUsuario = c.UsuarioId, // luego puedes resolver al nombre
                    IdArea = c.IdArea,
                    Area = c.Area.Nombre,
                    ResponsableUsuarioId = c.Area.ResponsableUsuarioId,
                    NombreResponsable = c.Area.ResponsableUsuarioId,
                    FechaInicio = c.FechaInicio,
                    Cerrada = c.Cerrada,
                    UltimoMensajeFecha = c.Mensajes
                        .OrderByDescending(m => m.Fecha)
                        .Select(m => (DateTime?)m.Fecha)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Json(new { ok = true, conversaciones = lista });
        }

        // ================== 6. MIS CONVERSACIONES COMO RESPONSABLE DE ÁREA ==================
        // GET /Chat/MisConversacionesArea
        [HttpGet("MisConversacionesArea")]
        public async Task<IActionResult> MisConversacionesArea()
        {
            var usuarioId = GetUsuarioIdActual();
            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized();

            var queryBase = _db.ChatConversaciones
                .Include(c => c.Area)
                .Include(c => c.Mensajes)
                .Where(c => c.Area.ResponsableUsuarioId == usuarioId);

            var lista = await queryBase
                .GroupBy(c => new
                {
                    c.UsuarioId,
                    c.IdArea,
                    AreaNombre = c.Area.Nombre,
                    c.Area.ResponsableUsuarioId
                })
                .Select(g => new ConversacionResumenViewModel
                {
                    IdConversacion = g
                        .OrderByDescending(c => c.FechaInicio)
                        .Select(c => c.IdConversacion)
                        .FirstOrDefault(),

                    UsuarioId = g.Key.UsuarioId,
                    NombreUsuario = g.Key.UsuarioId,
                    IdArea = g.Key.IdArea,
                    Area = g.Key.AreaNombre,
                    ResponsableUsuarioId = g.Key.ResponsableUsuarioId,
                    NombreResponsable = g.Key.ResponsableUsuarioId,
                    FechaInicio = g.Min(c => c.FechaInicio),
                    Cerrada = g.All(c => c.Cerrada),
                    UltimoMensajeFecha = g
                        .SelectMany(c => c.Mensajes)
                        .OrderByDescending(m => m.Fecha)
                        .Select(m => (DateTime?)m.Fecha)
                        .FirstOrDefault(),

                    // 👇 Aquí contamos mensajes NO leídos cuyo autor NO es el responsable
                    UnreadCount = g
                        .SelectMany(c => c.Mensajes)
                        .Count(m => !m.Leido && m.AutorUsuarioId != usuarioId)
                })
                .OrderByDescending(x => x.UltimoMensajeFecha ?? x.FechaInicio)
                .ToListAsync();

            return Json(new { ok = true, conversaciones = lista });
        }




        // === CERRAR CONVERSACIÓN ===
        [HttpPost("CerrarConversacion")]
        public async Task<IActionResult> CerrarConversacion([FromBody] CerrarConversacionDto dto)
        {
            if (dto == null || dto.Id <= 0)
                return BadRequest(new { ok = false, mensaje = "Id inválido." });

            var conv = await _db.ChatConversaciones
                .Include(c => c.Area)
                .FirstOrDefaultAsync(c => c.IdConversacion == dto.Id);

            if (conv == null)
                return NotFound(new { ok = false, mensaje = "La conversación no existe." });

            // (Opcional) validar que el usuario actual sea responsable del área
            var usuarioId = GetUsuarioIdActual();
            if (!string.Equals(conv.Area?.ResponsableUsuarioId, usuarioId, StringComparison.OrdinalIgnoreCase))
                return Forbid("No tienes permiso para cerrar esta conversación.");

            conv.Cerrada = true;
            await _db.SaveChangesAsync();

            return Json(new { ok = true });
        }

        [HttpGet("BandejaArea")]
        public IActionResult BandejaArea()
        {
            return View("~/Views/Chat/BandejaArea.cshtml");
        }

    }
}
