namespace Plataforma_CG.Models.SAP.JSON
{
    public class SolicitudSurtidoModel
    {
        public string ClienteId { get; set; }
        public string NombreCliente { get; set; }
        public string Comentario { get; set; }
        public DateTime FechaSurtido { get; set; }
        public string Serie { get; set; }
        public int ProcesoId { get; set; }

        public List<MultiAlmacenModel> Multialmacen { get; set; }
    }
}
