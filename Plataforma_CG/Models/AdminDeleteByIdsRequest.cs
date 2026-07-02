using System.Collections.Generic;

namespace Plataforma_CG.Models
{
    public class AdminDeleteByIdsRequest
    {
        public string Tipo { get; set; }              // CEDIS | VENDEDOR | CLIENTE
        public List<long> RowIds { get; set; }        // ids seleccionados
        public string Reason { get; set; }            // motivo opcional
    }
}
