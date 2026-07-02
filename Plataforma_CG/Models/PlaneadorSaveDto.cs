namespace Plataforma_CG.Models
{
    public class PlaneadorSaveDto
    {

        public DateTime FechaPlan { get; set; }
        public string TipoPlan { get; set; } = "PLAN";
        public string PlanTexto { get; set; } = "";

        public int? ProgramacionId { get; set; }           // ✅ NUEVO
        public string NombreProgramacion { get; set; } = "";// ✅ NUEVO

        public string Col1Label { get; set; } = "";
        public string Col2Label { get; set; } = "";
        public string Col3Label { get; set; } = "";
        public int[] TopCanales { get; set; } = Array.Empty<int>();
        public decimal[] TopKgCanal { get; set; } = Array.Empty<decimal>();
        public List<PlaneadorSaveRowDto> Rows { get; set; } = new();
    }

    public class PlaneadorSaveRowDto
    {

        public string? GroupKey { get; set; }
        public int? Nivel { get; set; }
        public int? Orden { get; set; }

        public string DesSku { get; set; } = "";
        public string DesProducto { get; set; } = "";
        public string Col1 { get; set; } = "0";
        public string Col2 { get; set; } = "0";
        public string Col3 { get; set; } = "0";
        public string RendPct { get; set; } = "";
        public string KgLote { get; set; } = "";
        public string Canales { get; set; } = "";
        public string InySku { get; set; } = "";
        public string InyProducto { get; set; } = "";
        public string InyPct { get; set; } = "";
        public string InyModo { get; set; } = "";
        public string Subtotal { get; set; } = "";
        public string Piezas { get; set; } = "";
        public string Almacen { get; set; } = "";
        public string Manejo { get; set; } = "";
        public string Etiquetado { get; set; } = "";
        public string Observaciones { get; set; } = "";
    }
}