namespace Plataforma_CG.Models.Operaciones.Pieles
{
    public class PielSeleccionadaModel
    {
        public long LoteId { get; set; }

        public string Lote { get; set; }

        public long ProduccionId { get; set; }

        public string CodigoEtiqueta { get; set; }

        public double PesoNeto { get; set; }

        public string FechaProduccion { get; set; }
    }
}
