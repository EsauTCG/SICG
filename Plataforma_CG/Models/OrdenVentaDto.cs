namespace Plataforma_CG.Models
{
    public class OrdenVentaDto
    {

        public int Id { get; set; }

        public int? PedidoVentaId { get; set; }
        public string Consecutivo { get; set; }
        public string Serie { get; set; }
        public DateTime? FechaEntrega { get; set; }

        public DateTime? FechaEmbarque { get; set; }
        public string AlmacenSurtir { get; set; }
        public DateTime? FechaRegistro { get; set; }
        public string Cliente { get; set; }

        public string ClienteNombre { get; set; }        // ← NUEVO (nombre legible)
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Presentacion { get; set; }
        public string Observacion { get; set; }
        public decimal? Saldo { get; set; }
        public decimal? OtrosPedidos { get; set; }
        public decimal? Credito { get; set; }
        public int Estatus { get; set; }
        public List<OrdenVentaDetalleDto> Productos { get; set; } = new();
    }
}
