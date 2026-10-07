using System;
using System.Collections.Generic;
using System.Linq;

namespace Plataforma_CG.ViewModels
{
    public sealed class OrdenVentaDocumentoViewModel
    {
        public int Id { get; set; }

        public string Consecutivo { get; set; } = string.Empty;

        public DateTime FechaRegistro { get; set; }

        public DateTime? FechaEntrega { get; set; }

        public DateTime? FechaEmbarque { get; set; }

        public string HoraEmbarque { get; set; } = string.Empty;

        public string Cliente { get; set; } = string.Empty;

        public string ClienteNombre { get; set; } = string.Empty;

        public string Vendedor { get; set; } = string.Empty;

        public string Serie { get; set; } = string.Empty;

        public string Ruta { get; set; } = string.Empty;

        public string Presentacion { get; set; } = string.Empty;

        public string Observacion { get; set; } = string.Empty;

        public int Estatus { get; set; }

        public string Siguiente { get; set; } = "salir";

        public List<OrdenVentaDocumentoLineaViewModel> Productos { get; set; }
            = new List<OrdenVentaDocumentoLineaViewModel>();

        public decimal TotalCajas =>
            Productos.Sum(x => x.Cajas);

        public decimal TotalPeso =>
            Productos.Sum(x => x.Peso);

        public decimal TotalImporte =>
            Productos.Sum(x => x.Importe);

        public string EstatusTexto =>
            Estatus switch
            {
                0 => "Cancelada",
                1 => "Pendiente",
                2 => "En autorización",
                3 => "Pendiente",
                4 => "Validada",
                5 => "Enviada a SAP",
                6 => "Logística",
                _ => "Registrada"
            };
    }

    public sealed class OrdenVentaDocumentoLineaViewModel
    {
        public int Numero { get; set; }

        public string ProductoCodigo { get; set; } = string.Empty;

        public string ProductoNombre { get; set; } = string.Empty;

        public decimal Cajas { get; set; }

        public decimal Peso { get; set; }

        public decimal Precio { get; set; }

        public decimal Importe => Peso * Precio;
    }
}