using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;


namespace Plataforma_CG.ViewModels
{
    public class AlmacenViewModel
    {


        public string? SelectedAlmacenId { get; set; }
        public List<SelectListItem> Almacenes { get; set; } = new(); // <- nunca null
    }
}
