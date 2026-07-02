using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using Plataforma_CG.Models.Operaciones.Planeacion;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Semanal
{
    public class TotalSemanalModel
    {
            public string FechaIn { get; set; }
            public string FechaFin { get; set; }

            public List<SemanaClasificacionModel> Canales { get; set; }

            public List<ParticipacionModel> Participaciones { get; set; }
            public string TipoPlan { get; set; }
        
    }
}
