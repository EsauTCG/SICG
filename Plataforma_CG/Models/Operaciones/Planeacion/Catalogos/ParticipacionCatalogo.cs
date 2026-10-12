using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;

public sealed class ParticipacionCatalogoFila
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public int ClasificacionId { get; set; }
    public decimal Porcentaje { get; set; }
    public int? SubClasificacionId { get; set; }
    public string? LineaCodigo { get; set; }
    public decimal? PartSub { get; set; }
}

public sealed class ParticipacionCatalogoRegistro
{
    public string Sku { get; set; } = "";
    public string? Nombre { get; set; }
    public int ClasificacionId { get; set; }
    public string? Clasificacion { get; set; }
    public string? Masters { get; set; }
    public decimal Minimo { get; set; }
    public decimal Maximo { get; set; }
    public int Registros { get; set; }
    public string? SubClasificaciones { get; set; }
    public string? UsuarioUltimoCambio { get; set; }
    public DateTime? FechaUltimoCambio { get; set; }
    [JsonIgnore] public string EstadoJson { get; set; } = "[]";
    public bool Mixto => Minimo != Maximo;
    public string? PorcentajeTexto => Mixto ? null : Minimo.ToString("0.00", CultureInfo.InvariantCulture);
    public string Version { get; set; } = "";
}

public sealed class ParticipacionCatalogoGuardar
{
    [Required, StringLength(7)] public string Sku { get; set; } = "";
    [Range(1, int.MaxValue)] public int ClasificacionId { get; set; }
    [Required, Range(typeof(decimal), "0", "100"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Porcentaje { get; set; }
    [Required, StringLength(64)] public string Version { get; set; } = "";
}

public sealed class ParticipacionCatalogoClasificacion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
}

public sealed class ParticipacionCatalogoConteos
{
    public int Skus { get; set; }
    public int Grupos { get; set; }
    public int Registros { get; set; }
}

public sealed record ParticipacionCatalogoListado(IReadOnlyList<ParticipacionCatalogoRegistro> Registros,
    int TotalFiltrado, int Pagina, int TamanoPagina, ParticipacionCatalogoConteos Conteos,
    IReadOnlyList<ParticipacionCatalogoClasificacion> Clasificaciones);

public sealed class ParticipacionCatalogoCambio
{
    public long Id { get; set; }
    public decimal? PorcentajeAntes { get; set; }
    public decimal PorcentajeDespues { get; set; }
    public int Registros { get; set; }
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
}

public sealed class ParticipacionCatalogoPagina
{
    public string UsuarioActual { get; set; } = "";
}
