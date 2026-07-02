namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public class EntradaModel
    {
        public int Id { get; set; }
        public string SKU { get; set; }
        public int fk_Inyectora { get; set; }
        public int Porcentaje { get; set; }
        public int ModoInyeccion { get; set; }
        public double Presion { get; set; }
        public int Velocidad { get; set; }
        public int Altura { get; set; }
        public string Avance { get; set; }
        public string Bascula { get; set; }
        public string FechaHora { get; set; }
        public string TipoPeso { get; set; }
        public int Autoriza { get; set; }
        public double Peso { get; set; }
        public double Tara { get; set; }
        public int fk_Lote { get; set; }
        public string Plantilla { get; set; }
        public string UsSIGO { get; set; }
        public string Folio { get; set; }

    }
}
