using System;
using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    // TABLA 1: LAS IPs
    public class ControlRedIp
    {
        [Key]
        public int Id { get; set; }
        [Required] public string Planta { get; set; }
        [Required] public string VlanId { get; set; }
        [Required] public string IpAddress { get; set; }
        public string EquipoAsignado { get; set; }
        public string TipoConexion { get; set; }
        public DateTime FechaAlta { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string ModificadoPor { get; set; }
        public string Observaciones { get; set; }
        public string? Usuario { get; set; }
        public string? Area { get; set; }
    }

    // TABLA 2: LAS VLANs
    public class VlanRed
    {
        [Key]
        public int IdInterno { get; set; }
        public string Planta { get; set; }
        public string VlanId { get; set; }
        public string Nombre { get; set; }
    }

    // TABLA 3: EL HISTORIAL
    public class LogMovimientoRed
    {
        [Key]
        public int IdLog { get; set; }
        public string Fecha { get; set; }
        public string Ip { get; set; }
        public string Accion { get; set; }
        public string Usuario { get; set; }
    }
}