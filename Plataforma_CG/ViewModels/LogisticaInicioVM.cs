using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    // ============================================================
    // VIEW MODEL PRINCIPAL
    // Vista:
    // Views/Surtido/Surtido_cedis.cshtml
    // ============================================================
    public class LogisticaInicioVM
    {
        // ----------------------------
        // Usuario SIGO
        // ----------------------------
        public int UsuarioId { get; set; }

        public string Login { get; set; } = "";

        public string NombreUsuario { get; set; } = "";


        // ----------------------------
        // Almacén activo
        // ----------------------------
        public int? AlmacenActivoId { get; set; }

        public string PlantaActiva { get; set; } = "";

        public string AlmacenActivoCodigo { get; set; } = "";

        public string AlmacenActivoNombre { get; set; } = "";

        public string ClasificacionActiva { get; set; } = "";

        public bool Layout3DConfirmado { get; set; }


        // ----------------------------
        // Permisos EN EL ALMACÉN ACTIVO
        // ----------------------------
        public bool PuedeMontacargas { get; set; }

        public bool PuedeCapturar { get; set; }

        public bool PuedeUbicar { get; set; }

        public bool PuedeCoordinar { get; set; }


        // ----------------------------
        // Todos los almacenes
        // autorizados para el usuario
        // ----------------------------
        public List<LogisticaAlmacenPermisoVM> Almacenes { get; set; }
            = new List<LogisticaAlmacenPermisoVM>();
    }


    // ============================================================
    // ALMACÉN + PERMISOS DEL USUARIO
    // ============================================================
    public class LogisticaAlmacenPermisoVM
    {
        // ----------------------------
        // Catálogo almacén
        // ----------------------------
        public int AlmacenId { get; set; }

        public string Codigo { get; set; } = "";

        public string Nombre { get; set; } = "";

        public string Planta { get; set; } = "";

        public string Clasificacion { get; set; } = "";

        public bool TieneLayout3D { get; set; }

        public string TipoLayout { get; set; } = "";


        // ----------------------------
        // Permisos
        // ----------------------------
        public bool PuedeMontacargas { get; set; }

        public bool PuedeCapturar { get; set; }

        public bool PuedeUbicar { get; set; }

        public bool PuedeCoordinar { get; set; }


        // ----------------------------
        // Configuración
        // ----------------------------
        public bool EsPredeterminado { get; set; }
    }
}
