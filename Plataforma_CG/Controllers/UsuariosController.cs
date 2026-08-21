using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    //[Authorize(Roles = "Administrador, Sistemas")]
    public class UsuariosController : Controller
    {
        private readonly Data.AppDbContextUsuarios _db;

        private readonly IConfiguration _configuracion;

        public UsuariosController(
            Data.AppDbContextUsuarios db,
            IConfiguration configuration)
        {
            _db = db;
            _configuracion = configuration;
        }


        // ============================================================
        // CARGAR COMBOS
        // ============================================================
        private async Task CargarCombosUsuarioAsync()
        {
            ViewBag.Perfiles = await _db.Perfiles
                .OrderBy(p => p.Nombre)
                .Select(p => new
                {
                    p.Id,
                    p.Nombre
                })
                .ToListAsync();


            ViewBag.Series = await _db.Series
                .AsNoTracking()
                .OrderBy(s => s.NombreSerie)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.NombreSerie
                })
                .ToListAsync();


            ViewBag.Almacenes =
                GetAlmacenes();
        }


        // ============================================================
        // ALMACENES DESDE appsettings.json -> Warehouses
        // ============================================================
        private List<SelectListItem> GetAlmacenes()
        {
            var lista =
                _configuracion
                    .GetSection("Warehouses")
                    .Get<List<WarehouseOption>>()
                ?? new List<WarehouseOption>();


            return lista
                .Where(w =>
                    !string.IsNullOrWhiteSpace(w.Id)
                    &&
                    !string.IsNullOrWhiteSpace(w.Name)
                )
                .Select(w =>
                    new SelectListItem
                    {
                        Value = w.Id,
                        Text = w.Name
                    }
                )
                .ToList();
        }


        // ============================================================
        // GUARDAR SERIES DEL USUARIO
        // ============================================================
        private async Task GuardarSeriesUsuarioAsync(
            int usuarioId,
            List<int>? seriesSeleccionadasIds)
        {
            var ids =
                seriesSeleccionadasIds?
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList()
                ?? new List<int>();


            var actuales =
                await _db.UsuarioSeries
                    .Where(x =>
                        x.UsuarioId == usuarioId
                    )
                    .ToListAsync();


            _db.UsuarioSeries.RemoveRange(
                actuales
            );


            foreach (var serieId in ids)
            {
                _db.UsuarioSeries.Add(
                    new UsuarioSerie
                    {
                        UsuarioId =
                            usuarioId,

                        SerieId =
                            serieId,

                        FechaAsignacion =
                            DateTime.Now
                    }
                );
            }


            await _db.SaveChangesAsync();
        }


        // ============================================================
        // LISTADO DE USUARIOS
        // ============================================================
        public async Task<IActionResult> UsuariosConfiguracion()
        {
            var usuarios =
                await _db.Usuarios

                    .Include(u =>
                        u.Perfil
                    )

                    .Include(u =>
                        u.UsuarioSeries
                    )
                        .ThenInclude(us =>
                            us.Serie
                        )

                    .OrderBy(u =>
                        u.Usuario
                    )

                    .ToListAsync();


            return View(
                usuarios
            );
        }


        // ============================================================
        // CREAR - GET
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            await CargarCombosUsuarioAsync();


            return View(
                new UsuarioSQL
                {
                    Activo = true,

                    SeriesSeleccionadasIds =
                        new List<int>()
                }
            );
        }


        // ============================================================
        // CREAR - POST
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> Crear(
            UsuarioSQL model)
        {
            var seriesSeleccionadas =
                model.SeriesSeleccionadasIds
                ?? new List<int>();


            var errores =
                ModelState

                    .Where(kvp =>
                        kvp.Value != null
                        &&
                        kvp.Value.Errors.Count > 0
                    )

                    .SelectMany(kvp =>
                        kvp.Value!.Errors.Select(err =>
                            $"{kvp.Key}: {err.ErrorMessage}"
                        )
                    )

                    .ToList();


            ViewData["ModelErrors"] =
                errores;


            if (!ModelState.IsValid)
            {
                await CargarCombosUsuarioAsync();

                TempData["Error"] =
                    "El modelo no es válido. Revisa los errores mostrados.";

                return View(
                    model
                );
            }


            if (await _db.Usuarios.AnyAsync(u =>
                    u.Usuario == model.Usuario))
            {
                ModelState.AddModelError(
                    "Usuario",
                    "Ya existe un usuario con este nombre."
                );

                await CargarCombosUsuarioAsync();

                return View(
                    model
                );
            }


            using var sha =
                SHA256.Create();


            model.Password =
                Convert.ToBase64String(
                    sha.ComputeHash(
                        Encoding.UTF8.GetBytes(
                            model.Password
                        )
                    )
                );


            model.Activo =
                true;


            model.AlmacenesPermitidos =
                JsonSerializer.Serialize(
                    model.AlmacenesSeleccionados
                    ?? new List<string>()
                );


            // Los permisos de logística quedan inicialmente
            // en FALSE, salvo que posteriormente el administrador
            // los active desde "Permisos Logística".
            model.LogisticaMontacarguista = false;
            model.LogisticaCapturista = false;
            model.LogisticaUbicador = false;
            model.LogisticaCoordinador = false;


            _db.Usuarios.Add(
                model
            );


            await _db.SaveChangesAsync();


            await GuardarSeriesUsuarioAsync(
                model.Id,
                seriesSeleccionadas
            );


            TempData["Exito"] =
                "Usuario creado correctamente.";


            return RedirectToAction(
                nameof(UsuariosConfiguracion)
            );
        }


        // ============================================================
        // CAMBIAR ESTADO
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> CambiarEstado(
            int id)
        {
            var user =
                await _db.Usuarios.FindAsync(
                    id
                );


            if (user == null)
            {
                return NotFound();
            }


            user.Activo =
                !user.Activo;


            await _db.SaveChangesAsync();


            return RedirectToAction(
                nameof(UsuariosConfiguracion)
            );
        }


        // ============================================================
        // EDITAR - GET
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Editar(
            int id)
        {
            var usuario =
                await _db.Usuarios

                    .Include(u =>
                        u.UsuarioSeries
                    )

                    .FirstOrDefaultAsync(u =>
                        u.Id == id
                    );


            if (usuario == null)
            {
                return NotFound();
            }


            usuario.SeriesSeleccionadasIds =
                usuario.UsuarioSeries

                    .Select(x =>
                        x.SerieId
                    )

                    .ToList();


            usuario.AlmacenesSeleccionados =
                JsonSerializer
                    .Deserialize<List<string>>(
                        usuario.AlmacenesPermitidos
                        ?? "[]"
                    )
                ?? new List<string>();


            await CargarCombosUsuarioAsync();


            return View(
                usuario
            );
        }


        // ============================================================
        // EDITAR - POST
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> Editar(
            UsuarioSQL model)
        {
            ModelState.Remove(
                "Password"
            );


            var seriesSeleccionadas =
                model.SeriesSeleccionadasIds
                ?? new List<int>();


            if (!ModelState.IsValid)
            {
                await CargarCombosUsuarioAsync();

                TempData["Error"] =
                    "El modelo no es válido. Revisa los errores mostrados.";

                return View(
                    model
                );
            }


            var usuarioDb =
                await _db.Usuarios.FindAsync(
                    model.Id
                );


            if (usuarioDb == null)
            {
                return NotFound();
            }


            usuarioDb.Nombre =
                model.Nombre;


            usuarioDb.PerfilId =
                model.PerfilId;


            usuarioDb.EsVendedor =
                model.EsVendedor;


            usuarioDb.VendedorId =
                model.VendedorId;


            usuarioDb.IgnoraFiltroSerieTransferencias =
                model.IgnoraFiltroSerieTransferencias;


            usuarioDb.FechaModificacion =
                DateTime.Now;


            usuarioDb.AlmacenesPermitidos =
                JsonSerializer.Serialize(
                    model.AlmacenesSeleccionados
                    ?? new List<string>()
                );


            // IMPORTANTE:
            // NO modificamos aquí los campos Logistica...
            //
            // Se administran exclusivamente desde:
            // GuardarPermisosLogistica()
            //
            // Así editar nombre, perfil, vendedor, series,
            // almacenes o contraseña NO borra permisos
            // logísticos accidentalmente.


            if (!string.IsNullOrWhiteSpace(
                    model.NuevaPassword))
            {
                using var sha =
                    SHA256.Create();


                usuarioDb.Password =
                    Convert.ToBase64String(
                        sha.ComputeHash(
                            Encoding.UTF8.GetBytes(
                                model.NuevaPassword
                            )
                        )
                    );
            }


            await _db.SaveChangesAsync();


            await GuardarSeriesUsuarioAsync(
                usuarioDb.Id,
                seriesSeleccionadas
            );


            TempData["Exito"] =
                "Usuario actualizado correctamente.";


            return RedirectToAction(
                nameof(UsuariosConfiguracion)
            );
        }


        // ============================================================
        // DTO PARA PERMISOS LOGÍSTICOS
        // ============================================================
        public sealed class GuardarPermisosLogisticaReq
        {
            public int Id { get; set; }

            public bool LogisticaMontacarguista { get; set; }

            public bool LogisticaCapturista { get; set; }

            public bool LogisticaUbicador { get; set; }

            public bool LogisticaCoordinador { get; set; }
        }


        // ============================================================
        // GUARDAR PERMISOS LOGÍSTICA
        //
        // POST:
        // /Usuarios/GuardarPermisosLogistica
        //
        // Se llama desde el modal de:
        // UsuariosConfiguracion.cshtml
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPermisosLogistica(
            GuardarPermisosLogisticaReq model)
        {
            if (model.Id <= 0)
            {
                TempData["Error"] =
                    "El usuario recibido no es válido.";

                return RedirectToAction(
                    nameof(UsuariosConfiguracion)
                );
            }


            var usuario =
                await _db.Usuarios
                    .FirstOrDefaultAsync(x =>
                        x.Id == model.Id
                    );


            if (usuario == null)
            {
                TempData["Error"] =
                    "No se encontró el usuario.";

                return RedirectToAction(
                    nameof(UsuariosConfiguracion)
                );
            }


            usuario.LogisticaMontacarguista =
                model.LogisticaMontacarguista;


            usuario.LogisticaCapturista =
                model.LogisticaCapturista;


            usuario.LogisticaUbicador =
                model.LogisticaUbicador;


            usuario.LogisticaCoordinador =
                model.LogisticaCoordinador;


            usuario.FechaModificacion =
                DateTime.Now;


            await _db.SaveChangesAsync();


            var permisosActivos =
                new List<string>();


            if (usuario.LogisticaMontacarguista)
            {
                permisosActivos.Add(
                    "Montacarguista"
                );
            }


            if (usuario.LogisticaCapturista)
            {
                permisosActivos.Add(
                    "Capturista"
                );
            }


            if (usuario.LogisticaUbicador)
            {
                permisosActivos.Add(
                    "Ubicador"
                );
            }


            if (usuario.LogisticaCoordinador)
            {
                permisosActivos.Add(
                    "Coordinador"
                );
            }


            var resumen =
                permisosActivos.Count > 0
                    ? string.Join(
                        ", ",
                        permisosActivos
                    )
                    : "Sin funciones logísticas";


            TempData["Exito"] =
                $"Permisos de logística actualizados para " +
                $"{usuario.Nombre}: {resumen}.";


            return RedirectToAction(
                nameof(UsuariosConfiguracion)
            );
        }
    }
}
