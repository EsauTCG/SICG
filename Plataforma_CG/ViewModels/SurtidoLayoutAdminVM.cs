namespace Plataforma_CG.ViewModels
{
    public sealed class SurtidoLayoutAdminVM
    {
        public List<SurtidoAlmacenVM> Almacenes { get; set; } = new();
        public SurtidoAlmacenVM Almacen { get; set; } = new();

        public List<SurtidoLayoutAlmacenAdminVM> Layouts { get; set; } = new();
        public SurtidoLayoutAlmacenAdminVM? LayoutActual { get; set; }

        public List<SurtidoColorAdminVM> Colores { get; set; } = new();
        public List<SurtidoMasterColorAdminVM> Masters { get; set; } = new();
        public List<SurtidoRotacionReglaAdminVM> ReglasRotacion { get; set; } = new();
        public List<SurtidoRackAdminVM> Racks { get; set; } = new();
        public List<SurtidoUbicacionAdminVM> Ubicaciones { get; set; } = new();
        public List<SurtidoColorResumenVM> ResumenColores { get; set; } = new();

        public int LayoutId => LayoutActual?.Id ?? 0;
        public int TotalUbicaciones => Ubicaciones.Count(x => x.Activo);
        public int UbicacionesOcupadas => Ubicaciones.Count(x => x.Activo && x.Ocupada);
        public int UbicacionesLibres => Math.Max(0, TotalUbicaciones - UbicacionesOcupadas);
    }

    public sealed class SurtidoLayoutAlmacenAdminVM
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public string CodigoLayout { get; set; } = "";
        public string NombreLayout { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public bool EsOperativo { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public int Racks { get; set; }
        public int Ubicaciones { get; set; }
        public int Ocupadas { get; set; }
    }

    public sealed class SurtidoColorAdminVM
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string CodigoColor { get; set; } = "";
        public string NombreColor { get; set; } = "";
        public string HexColor { get; set; } = "";
        public int Orden { get; set; }
        public bool Activo { get; set; }
    }

    public sealed class SurtidoMasterColorAdminVM
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string Master { get; set; } = "";
        public int ColorAlmacenId { get; set; }
        public string CodigoColor { get; set; } = "";
        public string NombreColor { get; set; } = "";
        public string HexColor { get; set; } = "";
        public bool Activo { get; set; }
    }

    public sealed class SurtidoRotacionReglaAdminVM
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string NombreRegla { get; set; } = "";
        public decimal? Desde { get; set; }
        public decimal? Hasta { get; set; }
        public bool AplicaSinRotacion { get; set; }
        public string ModoOrden { get; set; } = "";
        public int Prioridad { get; set; }
        public bool Activo { get; set; }
    }

    public sealed class SurtidoRackAdminVM
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string CodigoRack { get; set; } = "";
        public string NombreRack { get; set; } = "";
        public decimal PosX { get; set; }
        public decimal PosZ { get; set; }
        public decimal Ancho { get; set; }
        public decimal Alto { get; set; }
        public decimal Profundidad { get; set; }
        public decimal RotacionY { get; set; }
        public int Niveles { get; set; }
        public int PosicionesPorNivel { get; set; }
        public string HexColor { get; set; } = "";
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public int TotalUbicaciones { get; set; }
        public int UbicacionesOcupadas { get; set; }
    }

    public sealed class SurtidoUbicacionAdminVM
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public int ColorAlmacenId { get; set; }
        public string CodigoColor { get; set; } = "";
        public string NombreColor { get; set; } = "";
        public string HexColor { get; set; } = "";
        public int? RackId { get; set; }
        public string CodigoRack { get; set; } = "";
        public string Rack { get; set; } = "";
        public string Posicion { get; set; } = "";
        public string Altura { get; set; } = "";
        public string CodigoUbicacion { get; set; } = "";
        public string CodigoMapa3D { get; set; } = "";
        public int OrdenFisico { get; set; }
        public bool Activo { get; set; }
        public bool Ocupada { get; set; }
        public string UnidadCodigo { get; set; } = "";
    }

    public sealed class SurtidoColorResumenVM
    {
        public int ColorAlmacenId { get; set; }
        public string CodigoColor { get; set; } = "";
        public string NombreColor { get; set; } = "";
        public string HexColor { get; set; } = "";
        public int Total { get; set; }
        public int Ocupadas { get; set; }
        public int Libres => Math.Max(0, Total - Ocupadas);
    }

    public sealed class GuardarLayoutAlmacenReq
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public string CodigoLayout { get; set; } = "";
        public string NombreLayout { get; set; } = "";
        public string? Descripcion { get; set; }
        public bool EsOperativo { get; set; }
        public int Orden { get; set; } = 1;
        public bool Activo { get; set; } = true;
    }

    public sealed class ActivarLayoutAlmacenReq
    {
        public int LayoutId { get; set; }
        public string CodigoAlmacen { get; set; } = "";
    }

    public sealed class GuardarColorLayoutReq
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string CodigoColor { get; set; } = "";
        public string NombreColor { get; set; } = "";
        public string? HexColor { get; set; }
        public int Orden { get; set; } = 1;
        public bool Activo { get; set; } = true;
    }

    public sealed class GuardarMasterColorLayoutReq
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string Master { get; set; } = "";
        public int ColorAlmacenId { get; set; }
        public bool Activo { get; set; } = true;
    }

    public sealed class GuardarRotacionLayoutReq
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string NombreRegla { get; set; } = "";
        public decimal? Desde { get; set; }
        public decimal? Hasta { get; set; }
        public bool AplicaSinRotacion { get; set; }
        public string ModoOrden { get; set; } = "INICIO";
        public int Prioridad { get; set; } = 1;
        public bool Activo { get; set; } = true;
    }

    public sealed class GuardarRackLayoutReq
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public string CodigoRack { get; set; } = "";
        public string? NombreRack { get; set; }
        public decimal PosX { get; set; }
        public decimal PosZ { get; set; }
        public decimal Ancho { get; set; } = 10;
        public decimal Alto { get; set; } = 6;
        public decimal Profundidad { get; set; } = 2;
        public decimal RotacionY { get; set; }
        public int Niveles { get; set; } = 4;
        public int PosicionesPorNivel { get; set; } = 10;
        public string? HexColor { get; set; }
        public int Orden { get; set; } = 1;
        public bool Activo { get; set; } = true;
    }

    public sealed class GuardarUbicacionLayoutReq
    {
        public int Id { get; set; }
        public string CodigoAlmacen { get; set; } = "";
        public int LayoutId { get; set; }
        public int ColorAlmacenId { get; set; }
        public int? RackId { get; set; }
        public string? Rack { get; set; }
        public string? Posicion { get; set; }
        public string? Altura { get; set; }
        public string CodigoUbicacion { get; set; } = "";
        public string? CodigoMapa3D { get; set; }
        public int OrdenFisico { get; set; } = 1;
        public bool Activo { get; set; } = true;
    }
}
