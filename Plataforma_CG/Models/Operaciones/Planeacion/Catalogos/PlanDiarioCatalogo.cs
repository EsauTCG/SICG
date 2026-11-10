using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;

public sealed class PlanDiarioCatalogoFila
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public int ClasificacionId { get; set; }
    public decimal Porcentaje { get; set; }
    public int? SubClasificacionId { get; set; }
    public string? LineaCodigo { get; set; }
    public decimal? PartSub { get; set; }
}

public sealed class PlanDiarioCatalogoRegistro
{
    public string Sku { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Master { get; set; }
    public int ClasificacionId { get; set; }
    public string Clasificacion { get; set; } = "";
    public string? UsuarioUltimoCambio { get; set; }
    public DateTime? FechaUltimoCambio { get; set; }
    [JsonIgnore] public string EstadoJson { get; set; } = "[]";
    public IReadOnlyList<PlanDiarioCatalogoFila> Filas { get; set; } = [];
    public string Version { get; set; } = "";
}

public sealed class PlanDiarioCatalogoGuardar
{
    [Required, StringLength(64, MinimumLength = 64)] public string VersionCatalogo { get; set; } = "";
    [StringLength(50)] public string? SkuOriginal { get; set; }
    public int? ClasificacionOriginalId { get; set; }
    [StringLength(64)] public string? Version { get; set; }
    [Required, StringLength(50)] public string Sku { get; set; } = "";
    [Range(1, int.MaxValue)] public int ClasificacionId { get; set; }
    [Required, Range(typeof(decimal), "0", "100"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Porcentaje { get; set; }
    [StringLength(50)] public string? LineaCodigo { get; set; }
    [Required, MinLength(1), MaxLength(100)] public List<PlanDiarioCatalogoDistribucion> Distribuciones { get; set; } = [];
}

public sealed class PlanDiarioCatalogoDistribucion
{
    [Range(1, int.MaxValue)] public int SubClasificacionId { get; set; }
    [Required, Range(typeof(decimal), "0", "1"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? PartSub { get; set; }
}

public sealed class PlanDiarioCatalogoEliminar
{
    [Required, StringLength(64, MinimumLength = 64)] public string VersionCatalogo { get; set; } = "";
    [Required, StringLength(50)] public string Sku { get; set; } = "";
    [Range(1, int.MaxValue)] public int ClasificacionId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string Version { get; set; } = "";
}

public sealed class PlanDiarioCatalogoOpcion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Tipo { get; set; }
    public int? ClasificacionId { get; set; }
}

public sealed class PlanDiarioCatalogoArticulo
{
    public string Sku { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Master { get; set; }
}

public sealed class PlanDiarioCatalogoCambio
{
    public long Id { get; set; }
    public string Sku { get; set; } = "";
    public string? SkuAnterior { get; set; }
    public int ClasificacionId { get; set; }
    public int? ClasificacionAnteriorId { get; set; }
    public string Accion { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
    public int PlanesAfectados { get; set; }
    public string? DatosAntes { get; set; }
    public string? DatosDespues { get; set; }
}
