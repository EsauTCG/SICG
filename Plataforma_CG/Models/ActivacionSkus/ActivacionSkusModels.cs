using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models.ActivacionSkus;

public sealed record ActivacionSkusPagina(bool PuedeEscribir, bool PuedeEliminar, IReadOnlyList<PlantaActivacionSku> Plantas);
public sealed record PlantaActivacionSku(string Clave, string Nombre);
public sealed record ProgramacionSku(string ProveedorId, string Nombre, int Registros);
public sealed class CodigoSku
{
    // Conservar espacios: los tres campos forman la llave real de Codigo.
    public string Origen { get; set; } = "";
    public string AliasExterno { get; set; } = "";
    public string ArticuloId { get; set; } = "";
    public string Programacion { get; set; } = "";
    public string? Tipo { get; set; }
}

public sealed record ConteoTipoSku(string? Tipo, int Registros);
public sealed record ActivacionSkusListado(IReadOnlyList<CodigoSku> Registros, int Total,
    int Pagina, int TamanoPagina, IReadOnlyList<ConteoTipoSku> Conteos);
public sealed record ArticuloCatalogoSku(string ArticuloId, string? Descripcion);
public sealed record ActivacionSkusDisponibles(IReadOnlyList<ArticuloCatalogoSku> Registros, int Total,
    int Pagina, int TamanoPagina);
public sealed record ResultadoLoteSkus(int Seleccionados, int Modificados, int SinCambios);

public sealed record ArticuloSubclasificacionSku(string ArticuloId, string? Nombre, string? Clasifica2)
{
    public bool TieneN => Clasifica2?.Trim(' ') == "N";
    public bool PuedeCambiar => !string.IsNullOrWhiteSpace(ArticuloId)
        && (string.IsNullOrEmpty(Clasifica2?.Trim(' ')) || TieneN);
}

public sealed record SubclasificacionesSkusListado(IReadOnlyList<ArticuloSubclasificacionSku> Registros,
    int Total, int Pagina, int TamanoPagina, bool SoloConsulta);

public sealed class CambioSubclasificacionSku
{
    [Required, StringLength(15)]
    public string ArticuloId { get; set; } = "";
    [StringLength(50)]
    public string? Clasifica2Anterior { get; set; }
    [Required]
    public bool? AsignarN { get; set; }
}

public sealed class GuardarSubclasificacionSkus
{
    [Required, RegularExpression("^(TIF|P1)$")]
    public string Planta { get; set; } = "";
    [Required, MinLength(1), MaxLength(500)]
    public List<CambioSubclasificacionSku> Cambios { get; set; } = [];
}

public sealed class RegistroTipoSku
{
    [Required, StringLength(15)]
    public string Origen { get; set; } = "";
    [Required(AllowEmptyStrings = true), StringLength(15)]
    public string AliasExterno { get; set; } = "";
    [Required, StringLength(15)]
    public string ArticuloId { get; set; } = "";
    [StringLength(1)]
    public string? TipoAnterior { get; set; }
}

public sealed class CambiarTipoSkusMasivo
{
    [Required, RegularExpression("^(TIF|P1)$")]
    public string Planta { get; set; } = "";
    [Required, RegularExpression("^[PFCS]$")]
    public string TipoNuevo { get; set; } = "";
    [Required, MinLength(1), MaxLength(500)]
    public List<RegistroTipoSku> Registros { get; set; } = [];
}

public sealed class AgregarSkusMasivo
{
    [Required, RegularExpression("^(TIF|P1)$")]
    public string Planta { get; set; } = "";
    [Required, StringLength(15)]
    public string Origen { get; set; } = "";
    [Required, RegularExpression("^[PFCS]$")]
    public string Tipo { get; set; } = "";
    [Required, MinLength(1), MaxLength(500)]
    public List<string> Articulos { get; set; } = [];
}

public sealed class AgregarSkuProgramacion
{
    [Required, RegularExpression("^(TIF|P1)$")]
    public string Planta { get; set; } = "";
    [Required, StringLength(15)]
    public string Origen { get; set; } = "";
    [Required, StringLength(15)]
    public string ArticuloId { get; set; } = "";
    [Required, RegularExpression("^[PFCS]$")]
    public string Tipo { get; set; } = "";
}

public sealed class EliminarSkuProgramacion
{
    [Required, RegularExpression("^(TIF|P1)$")]
    public string Planta { get; set; } = "";
    [Required, StringLength(15)]
    public string Origen { get; set; } = "";
    [StringLength(15)]
    public string AliasExterno { get; set; } = "";
    [Required, StringLength(15)]
    public string ArticuloId { get; set; } = "";
    [StringLength(1)]
    public string? TipoAnterior { get; set; }
}

public sealed class CambiarTipoSku
{
    [Required, RegularExpression("^(TIF|P1)$", ErrorMessage = "Selecciona Planta TIF o Planta 1.")]
    public string Planta { get; set; } = "";
    [Required, StringLength(15)]
    public string Origen { get; set; } = "";
    [StringLength(15)]
    public string? OrigenNuevo { get; set; }
    // AliasExterno puede estar vacío en el sistema externo.
    [StringLength(15)]
    public string AliasExterno { get; set; } = "";
    [Required, StringLength(15)]
    public string ArticuloId { get; set; } = "";
    [StringLength(1)]
    public string? TipoAnterior { get; set; }
    [Required, RegularExpression("^[PFCS]$", ErrorMessage = "El tipo debe ser P, F, C o S.")]
    public string TipoNuevo { get; set; } = "";
}
