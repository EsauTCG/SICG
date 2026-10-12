namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public class EntradaModel
    {
        public int Id { get; set; }
        public string SKU { get; set; } = string.Empty;
        public int fk_Inyectora { get; set; }
        public int Porcentaje { get; set; }
        public int ModoInyeccion { get; set; }
        public decimal Presion { get; set; }
        public int Velocidad { get; set; }
        public int Altura { get; set; }
        public string Avance { get; set; } = string.Empty;
        public string Bascula { get; set; } = string.Empty;
        public string FechaHora { get; set; } = string.Empty;
        public string TipoPeso { get; set; } = string.Empty;
        public long Autoriza { get; set; }
        public decimal Peso { get; set; }
        public decimal Tara { get; set; }
        public long fk_Lote { get; set; }
        public string Plantilla { get; set; } = string.Empty;
        public string UsSIGO { get; set; } = string.Empty;
        public string Folio { get; set; } = string.Empty;

    }
}
