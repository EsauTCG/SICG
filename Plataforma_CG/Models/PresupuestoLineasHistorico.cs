namespace Plataforma_CG.Models
{
    public class PresupuestoLineaHistorico
    {
        public int Id { get; set; }

        // Cuándo se hizo el movimiento
        public DateTime FechaRegistro { get; set; }

        // OV / Cliente / Producto
        public int OrdenVentaId { get; set; }
        public string OrdenVentaConsecutivo { get; set; }

        public string ClienteId { get; set; }
        public string ClienteNombre { get; set; }

        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }

        // Periodo del presupuesto
        public int Mes { get; set; }
        public int Anio { get; set; }

        // Valores de presupuesto / consumo / autorización
        public decimal KilosPresupuestoMes { get; set; }      // lo que tenía asignado
        public decimal KilosConsumidosAntes { get; set; }     // consumo del mes antes de esta OV
        public decimal KilosSolicitadosLinea { get; set; }    // lo que pide esta línea
        public decimal KilosAutorizados { get; set; }         // lo que terminas autorizando

        // De dónde salió el presupuesto (CLIENTE / CEDIS / SIN PRESUPUESTO)
        public string FuentePresupuesto { get; set; }

        // Quién autorizó
        public string Usuario { get; set; }
    }

}
