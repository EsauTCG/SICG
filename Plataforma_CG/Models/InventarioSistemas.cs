namespace Plataforma_CG.Models
{
    public class InventarioSistemas
    {
        public int Id { get; set; }
        public string IdArticuloSap { get; set; }
        public string Nombre { get; set; }
        public string TipoArticulo { get; set; } //  "Activo Fijo" o "Consumible"
        public string Marca { get; set; }
        public string Modelo { get; set; }
        public string Proveedor { get; set; }
        public decimal Costo { get; set; }
        public DateTime? FechaCompra { get; set; }
        public int DiasGarantia { get; set; }
        public string NumeroSerie { get; set; }
        public string Asignacion { get; set; }
        public DateTime? FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
        public string TiempoVida { get; set; }
        public string Ubicacion { get; set; }
        public string Planta { get; set; }
        public int Stock { get; set; }
        public int StockMinimo { get; set; }
        public string FotoUsuario { get; set; }
        public string DocumentoComodato { get; set; }
        public string FirmaDigital { get; set; }
        public string? IP { get; set; } 
        public List<string> HistorialAsignaciones { get; set; } = new List<string>();
        public List<RegistroHistorial> RegistrosHistorial { get; set; } = new List<RegistroHistorial>();
        public bool EnRecuperacion { get; set; }

        public bool EnReparacion { get; set; }
        public string? MotivoFalla { get; set; }
        public string? BitacoraReparacion { get; set; }
        public string? FotoFalla { get; set; }
    }

    public class MovimientoInventario
    {
        public int Id { get; set; }
        public string ArticuloSap { get; set; }
        public string NombreArticulo { get; set; }
        public string TipoMovimiento { get; set; }
        public int Cantidad { get; set; }
        public DateTime Fecha { get; set; }
        public string Referencia { get; set; }
    }

    public class RegistroHistorial
    {
        public int Id { get; set; } 
        public int InventarioSistemasId { get; set; } 

        public string FechaHora { get; set; }
        public string Nota { get; set; }
        public string FotoBase64 { get; set; }
        public string DocumentoBase64 { get; set; }
        public string FirmaBase64 { get; set; }
    }
}