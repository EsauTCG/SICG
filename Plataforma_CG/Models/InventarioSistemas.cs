using System.ComponentModel.DataAnnotations.Schema;

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
        public string? AreaDestino { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public DateTime? FechaFinVida { get; set; }
        public DateTime? FechaDevolucion { get; set; }
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

    public class TransferenciaInventario
    {
        public int Id { get; set; }
        public int IdInventario { get; set; }
        public string IdArticuloSap { get; set; }
        public string Nombre { get; set; }
        public string PlantaOrigen { get; set; }
        public string PlantaDestino { get; set; }
        public string Estado { get; set; } // "ENVIADO" | "RECIBIDO"
        public int Cantidad { get; set; } = 1;
        public DateTime FechaEnvio { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public string? Nota { get; set; }
    }

    public class MarcaInventario
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public bool Activa { get; set; } = true;
    }

    public class AreaInventario
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public bool Activa { get; set; } = true;
    }

    [Table("AuditoriaInventario")]
    public class AuditoriaInventario
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Planta { get; set; }
        public string TipoFiltro { get; set; }
        public string Usuario { get; set; }
        public int Esperados { get; set; }
        public int Encontrados { get; set; }
        public int Faltantes { get; set; }
        public int Sobrantes { get; set; }
        public int Descuadres { get; set; }
        public int TotalDescuadre { get; set; }
        public bool Finalizada { get; set; }
    }

    [Table("AuditoriaInventarioDetalle")]
    public class AuditoriaInventarioDetalle
    {
        public int Id { get; set; }
        public int AuditoriaId { get; set; }
        public int IdInventario { get; set; }
        public string IdArticuloSap { get; set; }
        public string Nombre { get; set; }
        public string NumeroSerie { get; set; }
        public string TipoArticulo { get; set; }
        public int Esperado { get; set; }
        public int Escaneado { get; set; }
        public string Estado { get; set; }
        public int Diferencia { get; set; }
        public string Planta { get; set; }
    }

    [Table("AccionCorrectivaAuditoria")]
    public class AccionCorrectivaAuditoria
    {
        public int Id { get; set; }
        public int AuditoriaId { get; set; }
        public int IdInventario { get; set; }
        public string TipoAccion { get; set; }
        public int Cantidad { get; set; }
        public string Referencia { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
    }
}
