using Plataforma_CG.AccesoDatos.Operaciones.Inyeccion;
using Plataforma_CG.Models.Operaciones.Inyeccion;
namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    public class Lotes
    {
        AccesoLote al= new AccesoLote();
        public async Task<List<LotePlantillaModel>> ConsultarLotes()
        {
            var lista = new List<LotePlantillaModel>();
            lista = await al.ConsultarLotes();
            return lista;
        }
    }
}
