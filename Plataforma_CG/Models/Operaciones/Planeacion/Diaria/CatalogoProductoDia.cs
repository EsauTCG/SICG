using Plataforma_CG.Models.Operaciones.Planeacion.Extra;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria;

public sealed class CatalogoProductoDia
{
    public string ProductoCodigo { get; set; } = "";
    public PlanInyModel Iny { get; set; } = new();
    public PlanEtiModel Etiq { get; set; } = new();
    public List<ConversionProductoDia> Conversiones { get; set; } = new();
}

public sealed class ConversionProductoDia
{
    public string? SkuOrigen { get; set; }
    public string? SkuDestino { get; set; }
    public string? ProductoNombre { get; set; }
}
