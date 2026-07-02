using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Comercial.Ventas;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Comercial.Ventas;

namespace Plataforma_CG.Controllers.Comercial.Ventas
{
    public class ProspectoController : Controller
    {
        TodoVentasModel tvm = new TodoVentasModel();
        AccesoTodoVentas atv = new AccesoTodoVentas();

        
        public IActionResult Index()
        {
            var datos = atv._Prospectos.Listar();
            return PartialView("~/Views/Comercial/Ventas/Prospecto/Index.cshtml",datos);
        }
        public IActionResult Guardar()
        {
            return PartialView("~/Views/Comercial/Ventas/Prospecto/Guardar.cshtml");
        }
        public IActionResult Modificar(int id)
        {
            tvm._Prospecto = atv._Prospectos.ConsultarId(id);
            tvm._ListaVisitas = atv._Visitas.Listar(id);
            tvm._Visitas = new VisitasModel();

            return PartialView("~/Views/Comercial/Ventas/Prospecto/Modificar.cshtml",tvm);
        }
        [HttpPost]
        public async Task<IActionResult> Guardar(ProspectoModel model)
        {
            bool res = false;
            try
            {
                string uploadPathImg = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/imagen");
                string uploadPathAud = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/audio");
                string uploadPathPDF = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/pdf");

                if (!Directory.Exists(uploadPathImg))
                    Directory.CreateDirectory(uploadPathImg);
                if (!Directory.Exists(uploadPathAud))
                    Directory.CreateDirectory(uploadPathAud);
                if (!Directory.Exists(uploadPathPDF))
                    Directory.CreateDirectory(uploadPathPDF);
                // Guardar Imagen Fachada
                if (model.FotoFachada != null && model.FotoFachada.Length > 0)
                {
                    var nombreImagen = Guid.NewGuid().ToString() + Path.GetExtension(model.FotoFachada.FileName);
                    var rutaImagen = Path.Combine(uploadPathImg, nombreImagen);

                    using (var stream = new FileStream(rutaImagen, FileMode.Create))
                    {
                        await model.FotoFachada.CopyToAsync(stream);
                    }

                    model.RutaFotoFachada = "/uploads/ventas/prospectos/imagen/" + nombreImagen;
                }

                // Guardar Audio
                if (model.Audio != null && model.Audio.Length > 0)
                {
                    var nombreAudio = Guid.NewGuid().ToString() + Path.GetExtension(model.Audio.FileName);
                    var rutaAudio = Path.Combine(uploadPathAud, nombreAudio);

                    using (var stream = new FileStream(rutaAudio, FileMode.Create))
                    {
                        await model.Audio.CopyToAsync(stream);
                    }

                    model.AudioPath = "/uploads/ventas/prospectos/audio/" + nombreAudio;
                }
                if (model.ArcListaPrecios != null && model.ArcListaPrecios.Length > 0)
                {
                    var nombrePDF = Guid.NewGuid().ToString() + Path.GetExtension(model.ArcListaPrecios.FileName);
                    var rutaAudio = Path.Combine(uploadPathPDF, nombrePDF);

                    using (var stream = new FileStream(rutaAudio, FileMode.Create))
                    {
                        await model.ArcListaPrecios.CopyToAsync(stream);
                    }

                    model.ListaPrecios = "/uploads/ventas/prospectos/pdf/" + nombrePDF;
                }
                atv._Prospectos.Insertar(model);
                res = true;
            }
            catch (Exception)
            {

            }
            // Si el modelo no es válido, enviamos los errores
            //var errores = ModelState.Values
            //    .SelectMany(v => v.Errors)
            //    .Select(e => e.ErrorMessage)
            //    .ToList();

            return Json(new { success = res });
        }
        [HttpPost]
        public async Task<IActionResult> Modificar(TodoVentasModel model, bool EliminarFoto = false, bool EliminarAudio = false, bool EliminarPDF = false)
        {
            string uploadPathImg = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/imagen");
            string uploadPathAud = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/audio");
            string uploadPathPDF = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/pdf");

            if (!Directory.Exists(uploadPathImg))
                Directory.CreateDirectory(uploadPathImg);
            if (!Directory.Exists(uploadPathAud))
                Directory.CreateDirectory(uploadPathAud);
            if (!Directory.Exists(uploadPathPDF))
                Directory.CreateDirectory(uploadPathPDF);

            // --- FOTO ---
            if (EliminarFoto)
            {
                // Si hay archivo existente, eliminarlo físicamente
                if (!string.IsNullOrEmpty(model._Prospecto.RutaFotoFachada))
                {
                    var rutaFisica = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model._Prospecto.RutaFotoFachada.TrimStart('/'));
                    if (System.IO.File.Exists(rutaFisica))
                        System.IO.File.Delete(rutaFisica);
                }
                model._Prospecto.RutaFotoFachada = null;
            }
            else if (model._Prospecto.FotoFachada != null && model._Prospecto.FotoFachada.Length > 0)
            {
                var nombreImagen = Guid.NewGuid().ToString() + Path.GetExtension(model._Prospecto.FotoFachada.FileName);
                var rutaImagen = Path.Combine(uploadPathImg, nombreImagen);

                using (var stream = new FileStream(rutaImagen, FileMode.Create))
                {
                    await model._Prospecto.FotoFachada.CopyToAsync(stream);
                }

                model._Prospecto.RutaFotoFachada = "/uploads/ventas/prospectos/imagen/" + nombreImagen;
            }
            // Si no subió nada y no eliminó, se conserva el hidden field de RutaFotoFachada

            // --- AUDIO ---
            if (EliminarAudio)
            {
                if (!string.IsNullOrEmpty(model._Prospecto.AudioPath))
                {
                    var rutaFisica = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model._Prospecto.AudioPath.TrimStart('/'));
                    if (System.IO.File.Exists(rutaFisica))
                        System.IO.File.Delete(rutaFisica);
                }
                model._Prospecto.AudioPath = null;
            }
            else if (model._Prospecto.Audio != null && model._Prospecto.Audio.Length > 0)
            {
                var nombreAudio = Guid.NewGuid().ToString() + Path.GetExtension(model._Prospecto.Audio.FileName);
                var rutaAudio = Path.Combine(uploadPathAud, nombreAudio);

                using (var stream = new FileStream(rutaAudio, FileMode.Create))
                {
                    await model._Prospecto.Audio.CopyToAsync(stream);
                }

                model._Prospecto.AudioPath = "/uploads/ventas/prospectos/audio/" + nombreAudio;
            }
            if (EliminarPDF)
            {
                if (!string.IsNullOrEmpty(model._Prospecto.ListaPrecios))
                {
                    var rutaFisica = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", model._Prospecto.ListaPrecios.TrimStart('/'));
                    if (System.IO.File.Exists(rutaFisica))
                        System.IO.File.Delete(rutaFisica);
                }
                model._Prospecto.ListaPrecios = null;
            }
            else if (model._Prospecto.ArcListaPrecios != null && model._Prospecto.ArcListaPrecios.Length > 0)
            {
                var nombrePDF = Guid.NewGuid().ToString() + Path.GetExtension(model._Prospecto.ArcListaPrecios.FileName);
                var rutaPDF = Path.Combine(uploadPathPDF, nombrePDF);

                using (var stream = new FileStream(rutaPDF, FileMode.Create))
                {
                    await model._Prospecto.ArcListaPrecios.CopyToAsync(stream);
                }

                model._Prospecto.ListaPrecios = "/uploads/ventas/prospectos/pdf/" + nombrePDF;
            }
            // Si no subió nada y no eliminó, se conserva el hidden field de AudioPath
            var res=atv._Prospectos.Modificar(model._Prospecto);

            //var errores = ModelState.Values
            //    .SelectMany(v => v.Errors)
            //    .Select(e => e.ErrorMessage)
            //    .ToList();

            return Json(new { success = res });
        }
        [HttpPost]
        public async Task<IActionResult> Visitas(VisitasModel model)
        {
            // Si no subió nada y no eliminó, se conserva el hidden field de AudioPath
            //var res = atv._Prospectos.Modificar(model._Prospecto);
            string uploadPathImg = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/ventas/prospectos/visitas");

            if (!Directory.Exists(uploadPathImg))
                Directory.CreateDirectory(uploadPathImg);
            // Guardar Imagen Fachada
            if (model.Foto != null && model.Foto.Length > 0)
            {
                var nombreImagen = Guid.NewGuid().ToString() + Path.GetExtension(model.Foto.FileName);
                var rutaImagen = Path.Combine(uploadPathImg, nombreImagen);

                using (var stream = new FileStream(rutaImagen, FileMode.Create))
                {
                    await model.Foto.CopyToAsync(stream);
                }

                model.RutaFotoVisita = "/uploads/ventas/prospectos/visitas/" + nombreImagen;
            }
            var res = new AccesoVisitas().InsertarVisita(model);

            //var errores = ModelState.Values
            //    .SelectMany(v => v.Errors)
            //    .Select(e => e.ErrorMessage)
            //    .ToList();

            return Json(new { success = res });
        }
    }
}
