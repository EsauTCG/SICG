using System;
using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    // Representa la tabla dbo.Objetivos
    public class ObjetivoViewModel
    {
        public int ID { get; set; }
        public int ID_Perfil { get; set; }
        public int ID_Tipo_Objetivo { get; set; }
        public string Tipo_Periodo_Cumplimiento { get; set; }
        public DateTime Fecha_Desde { get; set; }
        public DateTime? Fecha_Hasta { get; set; }
        public string? Proveedor { get; set; }
        public string? ID_Cliente { get; set; }
        public int? UsuarioID_Vendedor { get; set; }
        public string? SKU { get; set; }
        public string? CC { get; set; }
        public string? Linea { get; set; }
        public string Descripcion_Objetivo { get; set; }
        public string Estado { get; set; }
        public int UsuarioID_Creacion { get; set; }
        public DateTime Fecha_Creacion { get; set; }
        public int? UsuarioID_Modificacion { get; set; }
        public DateTime? Fecha_Modificacion { get; set; }
        public int? UsuarioID_Aprueba { get; set; }
        public DateTime? Fecha_Aprueba { get; set; }
        public string LINEA { get; set; }

        // Propiedades extendidas para las vistas (JOINs)
        public string NombrePerfil { get; set; }
        public string NombreTipoObjetivo { get; set; }
        public string NombreArticulo { get; set; }

        // Relacion uno a muchos con los valores
        public List<ObjetivoValorViewModel> ValoresConfigurados { get; set; } = new List<ObjetivoValorViewModel>();
    }

    // Representa la tabla dbo.Objetivo_Valor
    public class ObjetivoValorViewModel
    {
        public int ID { get; set; }
        public int ID_Objetivo { get; set; }
        public string Tipo_Valor { get; set; }
        public string Unidad_Medida { get; set; }
        public decimal? Valor_Minimo { get; set; }
        public decimal? Valor_Maximo { get; set; }
        public decimal? Valor_Objetivo { get; set; }
    }

    // Representa la tabla dbo.Tipo_Objetivo
    public class TipoObjetivoViewModel
    {
        public int ID { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; }
    }
}