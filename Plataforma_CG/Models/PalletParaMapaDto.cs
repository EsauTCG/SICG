namespace Plataforma_CG.Models
{
    public class PalletParaMapaDto
    {

        public int? Consecutivo { get; set; }               // puede venir NULL por TRY_CONVERT
        public string? NombreCliente { get; set; }
        public string? Direccion { get; set; }
        public string? Vendedor { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public decimal? KilosCaja { get; set; }             // nullable
        public int? CajasTotalesSku { get; set; }           // nullable
        public int? PalletNo { get; set; }                  // nullable por seguridad
        public int? CajasEnPallet { get; set; }             // nullable por seguridad
        public decimal? KilosEnPallet { get; set; }         // nullable
        public long? OrdenEntrega { get; set; }             // nullable (DENSE_RANK)
        public string? OrdenEntregaKey { get; set; }
    }
}
