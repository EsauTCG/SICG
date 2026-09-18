using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    public class SurtidoInicioVM
    {
        public int UsuarioId { get; set; }

        public string Login { get; set; } = "";

        public string NombreUsuario { get; set; } = "";

        public List<string> PlantasPermitidas { get; set; }
            = new List<string>();

        public List<SurtidoAlmacenVM> Almacenes { get; set; }
            = new List<SurtidoAlmacenVM>();

        public string PlantaActiva { get; set; } = "";

        public string AlmacenActivoCodigo { get; set; } = "";

        public string AlmacenActivoNombre { get; set; } = "";

        public string ClasificacionActiva { get; set; } = "";

        public bool Layout3DConfirmado { get; set; }

        public bool PuedeMontacargas { get; set; }

        public bool PuedeCapturar { get; set; }

        public bool PuedeUbicar { get; set; }

        public bool PuedeCoordinar { get; set; }
    }


    public class SurtidoAlmacenVM
    {
        public string Codigo { get; set; } = "";

        public string Nombre { get; set; } = "";

        // Valor real del appsettings:
        // PLANTA 1 / TIF 776 / etc.
        public string Sucursal { get; set; } = "";

        // Valor normalizado:
        // P1 / TIF / POR DEFINIR
        public string Planta { get; set; } = "";

        // TIF / NO_TIF / OPERATIVO / PENDIENTE
        public string Clasificacion { get; set; } = "";

        public bool TieneLayout3D { get; set; }

        // Regla controlada por SIGO.dbo.SurtidoAlmacenConfiguracion
        public bool ObligaUbicacion { get; set; }

        // False significa que aún no se ha guardado una regla
        // para este almacén en SIGO.
        public bool TieneConfiguracionUbicacion { get; set; }

        public bool EsActivo { get; set; }
    }


    public class SurtidoAlmacenConfiguracionVM
    {
        public string Codigo { get; set; } = "";

        public string Nombre { get; set; } = "";

        public string Sucursal { get; set; } = "";

        public string Planta { get; set; } = "";

        public string Clasificacion { get; set; } = "";

        public bool ObligaUbicacion { get; set; }

        public bool TieneConfiguracionUbicacion { get; set; }
    }


    public class SurtidoConfiguracionAlmacenesVM
    {
        public string Usuario { get; set; } = "";

        public List<SurtidoAlmacenConfiguracionVM> Almacenes { get; set; }
            = new List<SurtidoAlmacenConfiguracionVM>();
    }



    public class SurtidoMapa3DVM
    {
        public SurtidoAlmacenVM Almacen { get; set; }
            = new SurtidoAlmacenVM();

        public string UbicacionInicial { get; set; } = "";

        public bool LayoutCompletoConfirmado =>
            Almacen?.TieneLayout3D ?? false;
    }

}
