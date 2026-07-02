using Plataforma_CG.AccesoDatos.Comercial.Planeacion.Semanal;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion
{
    public class AccesoTodoPlan
    {
        public AccesoClasificacion _Clasificacion = new AccesoClasificacion();
        public AccesoMaster _Master = new AccesoMaster();
        public AccesoParticipacion _Participacion = new AccesoParticipacion();
        public AccesoPlanDetalle _Detalle = new AccesoPlanDetalle();
        public AccesoPlaneacion _Planeacion = new AccesoPlaneacion();
        public AccesoTodoSemanal _TodoSemanal = new AccesoTodoSemanal();
    }
}
