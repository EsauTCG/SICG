using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.AccesoDatos.Operaciones.Inyeccion;
namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    public class Receta
    {
        AccesoRecetas ar = new AccesoRecetas();
        public async Task<List<ProductoModel>> ListarProductos(string plan)
        {
            var lista = await ar.ListarProductos(plan);
            return lista;
        }
        public async Task<RecetaModel> ObtenerReceta(string sku)
        {
            var dato = await ar.Receta(sku);
            return dato;
        }
        public async Task<List<TaraModel>> ObtenerTaras()
        {
            var lista = await ar.Taras();
            return lista;
        }
        public async Task<string> InsertarEntrada(EntradaModel model)
        {
            var dato = await ar.InsertarEntrada(model);
            return dato;
        }
        public async Task<EntradaModel> ConsultarEntrada(int id)
        {
            return await ar.ConsultarEntrada(id);
        }
    }
}
