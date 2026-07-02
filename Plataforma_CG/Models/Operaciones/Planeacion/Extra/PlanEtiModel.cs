using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Extra
{
    public class PlanEtiModel
    {
        public int Etiquetacion { get; set; }
        public string Nombre { get; set; }
        public string DiasCaducidad { get; set; }
    }
}
