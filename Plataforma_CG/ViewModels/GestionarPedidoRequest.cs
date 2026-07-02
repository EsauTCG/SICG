// Ruta sugerida: /ViewModels/GestionarPedidoRequest.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Plataforma_CG.ViewModels
{
    public class GestionarPedidoRequest
    {
        public int OrdenId { get; set; }
        public DateTime? FechaEmbarque { get; set; }
        public string? AlmacenSurtir { get; set; }

        public List<GestionarProductoItem> Productos { get; set; } = new();

        public class GestionarProductoItem
        {
            public string ProductoCodigo { get; set; } = string.Empty;
            public string ProductoNombre { get; set; } = string.Empty;
            public decimal KilosCaja { get; set; }
            public decimal Precio { get; set; }
            public int Cajas { get; set; }

            [JsonPropertyName("almacen")]              // 👈 asegura que el "almacen" (lowercase del JS) llegue
            public string? Almacen { get; set; }
            public bool Eliminado { get; set; }         // 👈 NUEVO (viene del front)
           
        }
    }
}
