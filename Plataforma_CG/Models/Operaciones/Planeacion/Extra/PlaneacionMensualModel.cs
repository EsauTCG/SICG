namespace Plataforma_CG.Models.Operaciones.Planeacion.Extra
{
    public class GuardarPlaneacionMensualModel
    {
        public DateTime Fecha { get; set; }
        public string Clasif { get; set; }

        public List<PlaneacionMensualSkuModel> Productos { get; set; } = [];
    }

    public class PlaneacionMensualSkuModel
    {
        public string ProductoCodigo { get; set; }

        public List<PlaneacionMensualSubclasModel> SubClasificaciones { get; set; } = [];

        public decimal KgInyeccion { get; set; }
    }

    public class PlaneacionMensualSubclasModel
    {
        public int SubClasificacionId { get; set; }

        public decimal Participacion { get; set; }
    }
}
