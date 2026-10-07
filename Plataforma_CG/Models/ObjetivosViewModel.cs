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
        //.Nullable: el formulario no siempre lo envia y la BD admite vacio.
        // Con nullable reference types, un string no-nullable es tratado como obligatorio
        // por el model binding aunque nunca se publique en el formulario.
        public string? Tipo_Periodo_Cumplimiento { get; set; }
        public DateTime Fecha_Desde { get; set; }
        public DateTime? Fecha_Hasta { get; set; }
        public string? Proveedor { get; set; }
        public string? ID_Cliente { get; set; }
        public int? UsuarioID_Vendedor { get; set; }
        public string? SKU { get; set; }
        public string? CC { get; set; }
        public string? Linea { get; set; }
        public string? Descripcion_Objetivo { get; set; }
        public string? Estado { get; set; }
        public int UsuarioID_Creacion { get; set; }
        public DateTime Fecha_Creacion { get; set; }
        public int? UsuarioID_Modificacion { get; set; }
        public DateTime? Fecha_Modificacion { get; set; }
        public int? UsuarioID_Aprueba { get; set; }
        public DateTime? Fecha_Aprueba { get; set; }

        // Propiedades extendidas para las vistas (JOINs)
        public string? NombrePerfil { get; set; }
        public string? NombreTipoObjetivo { get; set; }
        public string? NombreArticulo { get; set; }

        // Solo lectura: vienen de un LEFT JOIN, asi que pueden venir nullos.
        // Deben ser nullable o el POST los exigiria aunque no se envien.
        public string? UsuarioVendedor { get; set; }
        public string? NombreVendedor { get; set; }

        // Auditoria (JOINs con UsuarioSQL) para la vista de detalle
        public string? UsuarioCreacion { get; set; }
        public string? NombreCreador { get; set; }
        public string? UsuarioModificacion { get; set; }
        public string? NombreModificador { get; set; }
        public string? UsuarioAprueba { get; set; }
        public string? NombreAutorizador { get; set; }

        // Relacion uno a muchos con los valores
        public List<ObjetivoValorViewModel> ValoresConfigurados { get; set; } = new List<ObjetivoValorViewModel>();

        /// <summary>Pagina del tablero de la que se abrio la edicion, para volver a ella al guardar.</summary>
        public string? URLRetorno { get; set; }
    }

    // Representa la tabla dbo.Objetivo_Valor
    public class ObjetivoValorViewModel
    {
        public int ID { get; set; }
        public int ID_Objetivo { get; set; }

        public int? ID_Catalogo_ValorObjetivo { get; set; }

        public string? Tipo_Valor { get; set; }
        public string? Unidad_Medida { get; set; }
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

    // Filas del log / bitácora de objetivos (auditoría de creación, modificación y autorización)
    public class ObjetivoLogViewModel
    {
        public int ID { get; set; }
        public int ID_Perfil { get; set; }
        public int ID_Tipo_Objetivo { get; set; }
        public string NombrePerfil { get; set; }
        public string NombreTipoObjetivo { get; set; }
        public string Estado { get; set; }

        public DateTime Fecha_Creacion { get; set; }
        public string UsuarioCreacion { get; set; }
        public string NombreCreador { get; set; }

        public DateTime? Fecha_Modificacion { get; set; }
        public string UsuarioModificacion { get; set; }
        public string NombreModificador { get; set; }

        public DateTime? Fecha_Aprueba { get; set; }
        public string UsuarioAprueba { get; set; }
        public string NombreAutorizador { get; set; }
    }

    /// <summary>Objetivo Activo que esta por vencer o que ya vencio.</summary>
    public class VencimientoViewModel
    {
        public int ID { get; set; }
        public string NombrePerfil { get; set; }
        public string NombreTipoObjetivo { get; set; }
        public string Descripcion_Objetivo { get; set; }
        public DateTime Fecha_Desde { get; set; }
        public DateTime Fecha_Hasta { get; set; }
        public string Estado { get; set; }

        /// <summary>Negativo = ya vencido.</summary>
        public int DiasRestantes { get; set; }

        /// <summary>Duracion en días del periodo actual, para proponer el siguiente.</summary>
        public int DuracionDias { get; set; }

        public string NombreVendedor { get; set; }
        public string NombreArticulo { get; set; }
        public int TotalMetas { get; set; }
    }
}