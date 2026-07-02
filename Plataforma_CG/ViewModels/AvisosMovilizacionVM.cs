using System;
using System.Collections.Generic;
using System.Linq;

namespace Plataforma_CG.ViewModels
{
    public sealed class AvisosMovilizacionPageVM
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public string Cliente { get; set; } = "";
        public string Venta { get; set; } = "";
        public string Lote { get; set; } = "";
        public List<AvisoMovilizacionResumenVM> Rows { get; set; } = new List<AvisoMovilizacionResumenVM>();

        public int TotalSolicitudes
        {
            get { return Rows.Count; }
        }

        public int TotalCajas
        {
            get { return Rows.Sum(x => x.TotalCajas); }
        }

        public decimal TotalKg
        {
            get { return Rows.Sum(x => x.TotalKg); }
        }
    }

    public sealed class AvisoMovilizacionResumenVM
    {
        public string SolicitudSurtidoId { get; set; } = "";
        public string Venta { get; set; } = "";
        public string Cliente { get; set; } = "";
        public DateTime? FechaVenta { get; set; }
        public string Lotes { get; set; } = "";
        public int TotalPartidas { get; set; }
        public int TotalCajas { get; set; }
        public decimal TotalKg { get; set; }
        public DateTime? FechaSacrificioMin { get; set; }
        public DateTime? FechaSacrificioMax { get; set; }
        public DateTime? FechaProduccionMin { get; set; }
        public DateTime? FechaProduccionMax { get; set; }
        public DateTime? FechaCaducidadMin { get; set; }
        public DateTime? FechaCaducidadMax { get; set; }

        public string FechaVentaTxt
        {
            get { return FechaVenta.HasValue ? FechaVenta.Value.ToString("dd/MM/yyyy") : ""; }
        }

        public string FechaSacrificioTxt
        {
            get { return FormatRangoFecha(FechaSacrificioMin, FechaSacrificioMax); }
        }

        public string FechaProduccionTxt
        {
            get { return FormatRangoFecha(FechaProduccionMin, FechaProduccionMax); }
        }

        public string FechaCaducidadTxt
        {
            get { return FormatRangoFecha(FechaCaducidadMin, FechaCaducidadMax); }
        }

        private static string FormatRangoFecha(DateTime? min, DateTime? max)
        {
            if (min == null && max == null) return "";
            if (min != null && max == null) return min.Value.ToString("dd/MM/yyyy");
            if (min == null && max != null) return max.Value.ToString("dd/MM/yyyy");

            var a = min.Value.Date;
            var b = max.Value.Date;

            return a == b
                ? a.ToString("dd/MM/yyyy")
                : string.Format("{0:dd/MM/yyyy} - {1:dd/MM/yyyy}", a, b);
        }
    }

    public sealed class AvisoMovilizacionDetalleVM
    {
        public string Planta { get; set; } = "";
        public string SolicitudSurtidoId { get; set; } = "";
        public string Venta { get; set; } = "";
        public string Cliente { get; set; } = "";
        public DateTime? FechaVenta { get; set; }
        public string Sku { get; set; } = "";
        public string Producto { get; set; } = "";
        public string Lote { get; set; } = "";
        public DateTime? FechaSacrificio { get; set; }
        public DateTime? FechaProduccion { get; set; }
        public DateTime? FechaCaducidad { get; set; }
        public int CuentaDeEtiqueta { get; set; }
        public decimal SumaDeKg { get; set; }

        public string FechaVentaTxt
        {
            get { return FechaVenta.HasValue ? FechaVenta.Value.ToString("dd/MM/yyyy") : ""; }
        }

        public string FechaSacrificioTxt
        {
            get { return FechaSacrificio.HasValue ? FechaSacrificio.Value.ToString("dd/MM/yyyy") : ""; }
        }

        public string FechaProduccionTxt
        {
            get { return FechaProduccion.HasValue ? FechaProduccion.Value.ToString("dd/MM/yyyy") : ""; }
        }

        public string FechaCaducidadTxt
        {
            get { return FechaCaducidad.HasValue ? FechaCaducidad.Value.ToString("dd/MM/yyyy") : ""; }
        }
    }

    public sealed class AvisosMovilizacionPdfVM
    {
        public DateTime FechaGeneracion { get; set; } = DateTime.Now;
        public string Comentarios { get; set; } = "";
        public string Usuario { get; set; } = "";
        public List<AvisoMovilizacionDetalleVM> Rows { get; set; } = new List<AvisoMovilizacionDetalleVM>();

        public int TotalSolicitudes
        {
            get { return Rows.Select(x => x.SolicitudSurtidoId).Distinct().Count(); }
        }

        public int TotalPartidas
        {
            get { return Rows.Count; }
        }

        public int TotalCajas
        {
            get { return Rows.Sum(x => x.CuentaDeEtiqueta); }
        }

        public decimal TotalKg
        {
            get { return Rows.Sum(x => x.SumaDeKg); }
        }
    }

    public sealed class EnviarAvisosMovilizacionRequest
    {
        public string CorreoMedico { get; set; } = "";
        public string Comentarios { get; set; } = "";
        public List<string> Solicitudes { get; set; } = new List<string>();
    }
}