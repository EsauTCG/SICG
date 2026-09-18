using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.AccesoDatos.Operaciones.Inyeccion;
namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    public class Receta
    {
        private readonly AccesoRecetas ar;

        public Receta(AccesoRecetas accesoRecetas)
        {
            ar = accesoRecetas;
        }
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
        public async Task<EntradaModel> InsertarEntrada(EntradaModel model)
        {
            var dato = await ar.InsertarEntrada(model);
            return dato;
        }
        public async Task<EntradaModel?> ConsultarEntrada(int id)
        {
            return await ar.ConsultarEntrada(id);
        }
    }
}
