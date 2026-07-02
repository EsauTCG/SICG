using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.ViewModels
{
    public class PresupuestoCedisSaveVM
    {

        public string Canal { get; set; } = default!; // U_CANAL seleccionado
        public int Mes { get; set; }                  // hidden Mes
       
        public int Anio { get; set; }          // <- sin atributo aquí
        public List<PresupuestoCedisItemVM> Items { get; set; } = new();
    }
}
