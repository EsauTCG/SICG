using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("CatCategorias_AutoArticulos")]
    public class CategoriaModel
    {
        [Key]
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Criticidad { get; set; }
    }

    [Table("CatUsuarios_AutoArticulos")]
    public class UsuarioModel
    {
        [Key]
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Departamento { get; set; }
        public string TokenGafete { get; set; }
    }

    [Table("RelPermisos_AutoArticulos")]
    public class PermisoModel
    {
        public int UsuarioId { get; set; }
        public int CategoriaId { get; set; }

        // Propiedades de navegación para Entity Framework
        [ForeignKey("UsuarioId")]
        public UsuarioModel Usuario { get; set; }

        [ForeignKey("CategoriaId")]
        public CategoriaModel Categoria { get; set; }
    }
    [Table("Log_Excepciones_AutoArticulos")]
    public class LogExcepcionModel
    {
        [Key]
        public int Id { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public int UsuarioId { get; set; } 
        public string Supervisor { get; set; } = "SUPERVISOR_GENERAL"; 
        public string ArticuloIngresado { get; set; } 
        public int CategoriaId { get; set; } 

        [ForeignKey("UsuarioId")]
        public UsuarioModel Usuario { get; set; }

        [ForeignKey("CategoriaId")]
        public CategoriaModel Categoria { get; set; }
    }
    [Table("PinArticulos")]
    public class PinArticulosModel
    {
        [Key]
        public string Clave { get; set; } 
        public string Valor { get; set; } 
        public string Descripcion { get; set; }
    }
    public class GuardarPermisosDto
    {
        public int UsuarioId { get; set; }
        public List<int> CategoriasIds { get; set; } = new List<int>();
    }
}