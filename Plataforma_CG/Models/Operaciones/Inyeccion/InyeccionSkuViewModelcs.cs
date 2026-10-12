namespace Plataforma_CG.Models
{
    public class InyeccionSkuViewModel
    {
        public int Id { get; set; }
        public string SKU { get; set; } = string.Empty;
        public int? fk_Inyectora { get; set; }
        public int Porcentaje { get; set; }
        public int? ModoInyeccion { get; set; }
        public decimal? Presion { get; set; }
        public int? Velocidad { get; set; }
        public int? Altura { get; set; }
        public string? Avance { get; set; }
    }

    public class InyeccionBitacoraViewModel
    {
        public int Id { get; set; }
        public System.DateTime Fecha { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Accion { get; set; } = string.Empty;
        public int? IdReceta { get; set; }
        public string? SKU { get; set; }
        public string? DatosAntes { get; set; }
        public string? DatosDespues { get; set; }
    }
}
