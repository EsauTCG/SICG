namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public class RecetaModel
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
    }
}
