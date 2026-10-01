namespace Plataforma_CG.Models
{
    public class CreditoAutorizacionHistorico
    {
        public int Id { get; set; }

        public DateTime FechaRegistro { get; set; }

        public int OrdenVentaId { get; set; }

        public string OrdenVentaConsecutivo { get; set; } = "";

        public string? Serie { get; set; }

        public string? ClienteId { get; set; }

        public string? ClienteNombre { get; set; }

        public decimal ImportePedido { get; set; }

        public decimal LimiteCredito { get; set; }

        public decimal SaldoActual { get; set; }

        public decimal OtrosPedidos { get; set; }

        public decimal Disponible { get; set; }

        public decimal MontoExcedido { get; set; }

        public string Usuario { get; set; } = "";

        public string Accion { get; set; } = "AUTORIZADO";

        public string? Motivo { get; set; }

        public int? EstatusAnterior { get; set; }

        public int? EstatusNuevo { get; set; }
    }
}