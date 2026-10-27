namespace Plataforma_CG.Models.Operaciones.Pieles
{
    public class GuardarPielesSurtidoModel
    {
        public long SolicitudSurtidoId { get; set; }

        public List<PielSeleccionadaModel> Pieles { get; set; } =
            new List<PielSeleccionadaModel>();
    }
}
