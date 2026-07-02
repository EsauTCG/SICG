using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    public sealed class GuardarTransferenciaRequest
    {
        public int Id { get; set; }
        public List<GuardarTransferenciaItem> Items { get; set; } = new();
    }

    public sealed class GuardarTransferenciaItem
    {
        public int? DetalleId { get; set; }
        public string Sku { get; set; } = "";
        public decimal Kg { get; set; }

        public decimal? Cajas { get; set; }
    }
}
