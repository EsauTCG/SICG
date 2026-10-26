namespace Plataforma_CG.Models.Operaciones.Consumibles
{
    public class ConsumiblesModel
    {
        public string Articulo { get; set; }
        public string Caja { get; set; }
        public string Bolsa { get; set; }

        public int TotalCajas { get; set; }
        public int BolsasCaja { get; set; }
        public int PzaBolsa { get; set; }
        public int TotalPzas { get; set; }
        public double TotalKG { get; set; }
    }
}
