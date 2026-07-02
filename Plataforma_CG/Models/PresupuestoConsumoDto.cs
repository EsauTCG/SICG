namespace Plataforma_CG.Models
{
    public class PresupuestoConsumoDto
    {

        public string Origen { get; set; }
        public int MesConsulta { get; set; }
        public int AnioConsulta { get; set; }
        public string ClienteCodigo { get; set; }

        public int VendedorId { get; set; }

        public string Buscar { get; set; }
        public string NombreCliente { get; set; }
        public string Canal { get; set; }
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }

        public string VendedorNombre { get; set; }
        public decimal PresupuestoAsignado { get; set; }

        public decimal? DisponibleVenta { get; set; }
        public decimal KgPedidosMes { get; set; }
        public decimal PresupuestoDisponible { get; set; }

        public decimal KgSurtidoReal { get; set; }

        public decimal? PlanProduccion { get; set; }
        public decimal? Producido { get; set; }

        public string U_MASTER { get; set; }

        public int? ClasificacionId { get; set; }
        public string ClasificacionNombre { get; set; }
        public decimal? TendenciaProduccion { get; set; } // opcional si la usas

    }
}
