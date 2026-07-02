namespace Plataforma_CG.Models.Comercial.Ventas
{
    public class AutorizacionModel
    {
        public int Id { get; set; }
        public long fk_Orden { get; set; }
        public int Autoriza { get; set; }
        public string Razon { get; set; }
        public string FechaHora { get; set; }
        public string Usuario { get; set; }
    }
}
