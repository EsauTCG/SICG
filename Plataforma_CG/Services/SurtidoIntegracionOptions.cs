namespace Plataforma_CG.Services;

public sealed class SurtidoIntegracionOptions
{
    public bool HabilitarEvaluacionAutomatica { get; set; } = false;
    public int IntervaloSegundos { get; set; } = 60;
    // Por defecto no asumir que CommerciaNET y Meat comparten instancia.
    public bool CommerciaEnMismaInstanciaQueMeat { get; set; } = false;
    public int MaximoPorCiclo { get; set; } = 10;
}
