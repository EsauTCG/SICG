namespace Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;

public sealed class AfectacionesDefinicion
{
    public string Version { get; set; } = "";
    public Dictionary<string,AfectacionesPlan> Planes { get; set; } = [];
}
public sealed class AfectacionesPlan
{
    public List<string> Conocidos { get; set; } = [];
    public List<AfectacionesDescuento> Descuentos { get; set; } = [];
    public List<AfectacionesSuma> Sumas { get; set; } = [];
    public List<AfectacionesBloqueo> Bloqueados { get; set; } = [];
}
public sealed class AfectacionesDescuento
{
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public List<string> Subclasificaciones { get; set; } = [];
}
public sealed class AfectacionesSuma
{
    public string Destino { get; set; } = "";
    public List<string> Fuentes { get; set; } = [];
    public List<string> Restar { get; set; } = [];
    public List<string> Subclasificaciones { get; set; } = [];
}
public sealed class AfectacionesBloqueo
{
    public string Sku { get; set; } = "";
    public bool Excluir { get; set; }
    public string Motivo { get; set; } = "";
}
public sealed record AfectacionesSubclasificacion(int Id, string Nombre);
public sealed record AfectacionesValor(string Sku, int SubClasificacionId, decimal PartSub, string? LineaCodigo = null);
public sealed record AfectacionesCambio(string Sku, int SubClasificacionId, string Subclasificacion, decimal Antes, decimal Despues, string Motivo);
public sealed record AfectacionesPendiente(string Sku, int SubClasificacionId, string Subclasificacion, decimal Valor, IReadOnlyList<string> Fuentes);
public sealed record AfectacionesResultado(IReadOnlyList<AfectacionesValor> Valores, IReadOnlyList<AfectacionesCambio> Cambios, IReadOnlyList<AfectacionesPendiente> Pendientes);
