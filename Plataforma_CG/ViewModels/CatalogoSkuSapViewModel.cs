namespace Plataforma_CG.ViewModels
{

    //MODELO PARA SACAR INFORMACION DE CATALOGO DIRECTAMENTE DE SAP 
    public class CatalogoSkuSapViewmodel
    {

        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? U_MASTER { get; set; }
        public string? U_TipoporSKU { get; set; }

        public decimal? U_KilosCaja { get; set; } // ⬅️ nuevo

        // ✅ NUEVOS
        public int? U_Clas_Prod { get; set; }
        public int? U_PRESENT { get; set; }
        public int? U_PorcInye { get; set; }


    }
}
