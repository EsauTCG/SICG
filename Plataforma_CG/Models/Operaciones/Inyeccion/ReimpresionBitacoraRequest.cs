namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public sealed class ReimpresionBitacoraRequest
    {
        public Guid SolicitudGuid { get; set; }
        public string IpImpresora { get; set; } = string.Empty;
    }
}
