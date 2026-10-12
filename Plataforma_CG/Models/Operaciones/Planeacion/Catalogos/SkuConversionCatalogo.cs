using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;

public sealed class SkuConversionRegistro
{
    public int Id { get; set; }
    public string SkuOrigen { get; set; } = "";
    public string? NombreOrigen { get; set; }
    public string SkuDestino { get; set; } = "";
    public string? NombreDestino { get; set; }
    public decimal? Factor { get; set; }
    public string? FactorTexto => Factor?.ToString("0.######", CultureInfo.InvariantCulture);
    public int? Prioridad { get; set; }
    public string? Motivo { get; set; }
    public bool Activo { get; set; }
    public DateTime FechaAlta { get; set; }
    public string? UsuarioAlta { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UsuarioUltimoCambio { get; set; }
    public string Version { get; set; } = "";
}

public sealed class SkuConversionGuardar
{
    [Range(0, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(50)] public string SkuOrigen { get; set; } = "";
    [Required, StringLength(50)] public string SkuDestino { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public decimal? Factor { get; set; }
    [Range(0, int.MaxValue)] public int? Prioridad { get; set; }
    [StringLength(150)] public string? Motivo { get; set; }
    public bool Activo { get; set; } = true;
    [StringLength(64)] public string? Version { get; set; }
}

public sealed class SkuConversionEstado
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    public bool Activo { get; set; }
    [Required, StringLength(64)] public string Version { get; set; } = "";
}

public sealed class SkuConversionConteos
{
    public int Total { get; set; }
    public int Activas { get; set; }
    public int Inactivas { get; set; }
}

public sealed record SkuConversionListado(IReadOnlyList<SkuConversionRegistro> Registros,
    int TotalFiltrado, int Pagina, int TamanoPagina, SkuConversionConteos Conteos);

public sealed class SkuConversionArticulo
{
    public string Sku { get; set; } = "";
    public string? Nombre { get; set; }
}

public sealed class SkuConversionCambio
{
    public long Id { get; set; }
    public string Accion { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string? DatosAntes { get; set; }
    public string DatosDespues { get; set; } = "";
}

public sealed class SkuConversionPagina
{
    public string UsuarioActual { get; set; } = "";
}
