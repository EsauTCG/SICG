using System;
using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    public sealed class CartaPorteInternaVm
    {
        public string Source { get; set; } = "P1";
        public string ClienteCodigo { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string Sucursal { get; set; } = "";
        public List<int> Solicitudes { get; set; } = new();
        public List<string> Pedidos { get; set; } = new();
        public DateTime? FechaValidacion { get; set; }
        public int TotalCajas { get; set; }
        public int TotalTarimas { get; set; }
        public List<string> Referencias { get; set; } = new();
        public List<CartaPorteMercanciaVm> Mercancias { get; set; } = new();
        public decimal PesoTotalKg { get; set; }
        public DateTime FechaPreparacion { get; set; } = DateTime.Now;
        // Identificador de esta generación; no es un UUID fiscal ni queda persistido.
        public string FolioInterno { get; set; } = "";
        // QR informativo, sin conexión con validación del SAT.
        public string CodigoQrInternoBase64 { get; set; } = "";
    }

    public sealed class CartaPorteMercanciaVm
    {
        public string Codigo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Unidad { get; set; } = "KGM";
        public decimal PesoKg { get; set; }
        public decimal Cajas { get; set; }
        public string Referencia { get; set; } = "";

        public string DescripcionCompleta
        {
            get
            {
                var codigo = (Codigo ?? "").Trim();
                var nombre = (Descripcion ?? "").Trim();
                if (nombre.Length == 0 || nombre.Equals(codigo, StringComparison.OrdinalIgnoreCase))
                    return codigo;
                if (nombre.StartsWith(codigo + " ", StringComparison.OrdinalIgnoreCase))
                    return nombre;
                return (codigo + " " + nombre).Trim();
            }
        }
    }
}
