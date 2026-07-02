namespace Plataforma_CG.Models
{
    public class AvisoMovilizacionDTO
    {
        public string sku { get; set; }
        public string producto { get; set; }
        public string lote { get; set; }
        public string fecha_sacrificio { get; set; }
        public string fecha_produccion { get; set; }
        public string fecha_caducidad { get; set; }
        public int Cuenta_de_etiqueta { get; set; }
        public decimal Suma_de_kg { get; set; }
    }
}
