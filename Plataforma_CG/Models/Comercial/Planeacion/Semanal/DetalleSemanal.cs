namespace Plataforma_CG.Models.Comercial.Planeacion.Semanal
{
    public class DetalleSemanal
    {
        public int Id { get; set; }
        public string ProductoCodigo { get; set; }
        public double Porcentaje { get; set; }
        public double Peso { get; set; }
        public int fk_Semana { get; set; }
    }
}
