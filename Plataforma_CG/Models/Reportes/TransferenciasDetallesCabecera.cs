namespace Plataforma_CG.Models.Reportes
{
    public class TransferenciasDetallesCabecera
    {
        public int? Id { get; set; }

        public string? Consecutivo { get; set; }

        public string? Sucursal { get; set; }

        public DateTime? FechaSolicitud { get; set; }

        public int? Mes { get; set; }

        public int? anio { get; set; }

        public string? Observacion { get; set; }

        public int? Estatus { get; set; }

        public string? UsuarioSolicita { get; set; }

        public DateTime? FechaCreacion { get; set; }

        public string? ProductoCodigo { get; set; }

        public string? ProductoNombre { get; set; }

        public decimal? CantidadKg { get; set; }

        public string? Nota { get; set; }

        public decimal? Cajas { get; set; }

        public bool? AutorizacionPresupuestoLinea { get; set; }
    }
}
