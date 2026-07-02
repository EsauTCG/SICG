namespace Plataforma_CG.ViewModels
{

    public class ClientePropiedadViewModel
    {
        public string Nombre { get; set; }   // Ej: "ConFactura"
        public bool Valor { get; set; }      // true/false según SAP
    }

    public class ClienteViewModel
    {
        public string CardCode { get; set; }
        public string CardName { get; set; }
        public string CardFName { get; set; }

        public string Vendedor { get; set; }

        public decimal CreditLimit { get; set; }
        public decimal CurrentAccountBalance { get; set; }

        public decimal SaldoVencido { get; set; } // 🔹 Nuevo

        // Campos de SAP
        public decimal TotalPendiente { get; set; } // suma de entregas + pedidos

        public string Display => $"{CardCode} - {CardName}";

        // 🔹 Aquí las propiedades dinámicas
        public List<ClientePropiedadViewModel> Propiedades { get; set; } = new List<ClientePropiedadViewModel>();
    }
}
