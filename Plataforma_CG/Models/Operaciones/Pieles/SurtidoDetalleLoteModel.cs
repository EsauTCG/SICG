namespace Plataforma_CG.Models.Operaciones.Pieles
{
    public class SurtidoDetalleLoteModel
    {
        public long SolicitudSurtidoId { get; set; }
        public int LoteId { get; set; }
        public string Almacen { get; set; }
        public int Unidades { get; set; }
        public int Peso { get; set; }
        public string FechaHora { get; set; }
    }
}
