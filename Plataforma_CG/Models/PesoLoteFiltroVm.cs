using System;
using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    public class PesoLoteFiltroVm
    {
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }

        // ✅ multi-select
        public List<int> LotesSeleccionados { get; set; } = new();

        // ✅ para llenar el dropdown (texto)
        public List<string> Lotes { get; set; } = new();
        // ejemplo de texto: "DEST00002320 (61108)"

        public string TipoPeso { get; set; } // MANUAL | AUTOMATICO | null

        public List<PesoLoteEncRow> Resultados { get; set; } = new();
    }
}
