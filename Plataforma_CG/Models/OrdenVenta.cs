using Plataforma_CG.Models; // Ajusta según tu proyecto
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class OrdenVenta
    {
        public int Id { get; set; }     
        public string Consecutivo { get; set; }
        public string Serie { get; set; }
        public DateTime FechaEntrega { get; set; }
        public DateTime? FechaEmbarque { get; set; }

        public DateTime? HoraEmbarque { get; set; }
        public string Cliente { get; set; }
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Presentacion { get; set; }
        public string? Observacion { get; set; }
        public decimal Saldo { get; set; }
        public decimal OtrosPedidos { get; set; }
        public decimal Credito { get; set; }

        [NotMapped]
        public string NombreCliente { get; set; }


        public DateTime? FechaRegistro { get; set; }

        public int Estatus { get; set; }


        public string Documentacion { get; set; }


        // ✅ Nuevos campos para autorizaciones individuales
        public bool AutorizacionPresupuesto { get; set; } = false;
        public bool AutorizacionPrecio { get; set; } = false;
        public bool AutorizacionCredito { get; set; } = false;

        public string? ModoPresupuesto { get; set; } // "CLIENTE" | "VENDEDOR"
  
        public int? VendedorId { get; set; }



        // 🔑 Relación uno a muchos
        public ICollection<OrdenVentaProducto> Productos { get; set; } = new List<OrdenVentaProducto>();


     


    }
}