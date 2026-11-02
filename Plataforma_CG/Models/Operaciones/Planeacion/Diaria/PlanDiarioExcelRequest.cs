namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria;

public sealed class PlanDiarioExcelRequest
{
    public string Fecha { get; set; } = "";
    public string TipoPlan { get; set; } = "";
    public List<CanalPlanExcel> Canales { get; set; } = new();
    public List<ProductoPlanExcel> Productos { get; set; } = new();
}

public sealed class CanalPlanExcel
{
    public int SubClasificacionId { get; set; }
    public string Nombre { get; set; } = "";
    public int Canales { get; set; }
    public decimal PesoPromedio { get; set; }
}

public sealed class ProductoPlanExcel
{
    public string Sku { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Master { get; set; } = "";
    public decimal Porcentaje { get; set; }
    public string SkuConversion { get; set; } = "";
    public string NombreConversion { get; set; } = "";
    public decimal PorcentajeInyeccion { get; set; }
    public string Etiqueta { get; set; } = "";
    public string DiasCaducidad { get; set; } = "";
    public List<ParticipacionPlanExcel> Participaciones { get; set; } = new();
}

public sealed class ParticipacionPlanExcel
{
    public int SubClasificacionId { get; set; }
    public decimal Proporcion { get; set; }
}
