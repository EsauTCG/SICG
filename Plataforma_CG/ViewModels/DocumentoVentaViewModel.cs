namespace Plataforma_CG.ViewModels
{
    public class DocumentoVentaViewModel
    {
        public DateTime DocDate { get; set; }
        public string SKU { get; set; }
        public decimal Kilos { get; set; }

        public string TipoDocumento { get; set; }  // "Factura", "Entrega", "NotaCredito"
        public decimal Quantity { get; set; }      // Cantidad de la línea

        // Campos nuevos para manejar cancelaciones
        public int DocEntry { get; set; }             // Identificador de la factura en SAP
        public bool Cancelled { get; set; }           // true si la factura está cancelada
        public int? RelatedInvoice { get; set; }      // DocEntry de la factura que reemplaza a la cancelada

        // 🔹 Nueva propiedad que viene de SAP (OINV.CANCELED)
        public string CANCELED { get; set; }

        public string  DocumentStatus { get; set; }

        public string LineStatus { get; set; }

        public string CardCode { get; set; } = "";

        public int LineNum { get; set; }

        public DateTime? ShipDate { get; set; }   // ← agregar
    }
}
