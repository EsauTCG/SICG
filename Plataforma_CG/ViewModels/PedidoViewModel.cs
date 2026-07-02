using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Plataforma_CG.ViewModels
{
    public class PedidoViewModel
    {
        public string Consecutivo { get; set; }
        public string Serie { get; set; } // Valor seleccionado
        public IEnumerable<SelectListItem> Series { get; set; } // Lista para el dropdown


        public string Presentacion { get; set; } // Valor seleccionado
        public IEnumerable<SelectListItem> Presentaciones { get; set; } // Lista para el dropdown

        [DataType(DataType.Date)]
        public DateTime? FechaEntrega { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaEmbarque { get; set; }
        public DateTime? HoraEmbarque { get; set; }

        // Cliente seleccionado
        public string Cliente { get; set; }

        // 👇 Lista para llenar el combo de clientes
        public List<ClienteViewModel> Clientes { get; set; } = new List<ClienteViewModel>();

        // 👇 Nueva propiedad
        public IEnumerable<SelectListItem> SeriesDisponibles { get; set; } = new List<SelectListItem>();


        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public bool ConFactura { get; set; }
       
        public string Observacion { get; set; }

        public decimal Saldo { get; set; }
        public decimal OtrosPedidos { get; set; }
        public decimal Credito { get; set; }
        public decimal SaldoVencido { get; set; } // 🔹 Nuevo


        public DateTime? FechaRegistro { get; set; }

        public int Estatus { get; set; }

        public string Documentacion { get; set; }

        // NUEVAS PROPIEDADES
        public string RangoTiempo { get; set; }  // "15", "30" o "custom"
        public int? DiasPersonalizados { get; set; } // si el rango es custom
        public int Mes { get; set; }  // mes del presupuesto
        public int Anio { get; set; } // año del presupuesto

        // ✅ NUEVO: el modo viaja en el POST
        public string? ModoPresupuesto { get; set; } // "VENDEDOR" | "CLIENTE"

        public List<PedidoProductoViewModel> Productos { get; set; } = new List<PedidoProductoViewModel>();
    }

    public class PedidoProductoViewModel
    {


        public string ProductoCodigo { get; set; }  // ← código del producto
        public string ProductoNombre { get; set; }  // ← nombre del producto    
        public string Producto { get; set; }
        public int Cajas { get; set; }

        public decimal PresupuestoDisponible { get; set; }

        public decimal KilosCaja { get; set; }
        public decimal Peso { get; set; }
        public decimal Precio { get; set; }
        public decimal Importe => Peso * Precio;
        public decimal Presupuesto { get; set; }

        public decimal VariacionPresupuesto { get; set; }


    }

}