using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{

    //MODELO PARA SACAR INFORMACION DE CATALOGO DIRECTAMENTE DE SAP 
    public class CatalogoSkuSapViewmodel
    {

        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? InventoryUOM { get; set; }
        public string? U_MASTER { get; set; }
        public string? U_TipoporSKU { get; set; }

        public decimal? U_KilosCaja { get; set; } // ⬅️ nuevo

        // ✅ NUEVOS
        public int? U_Clas_Prod { get; set; }
        public int? U_PRESENT { get; set; }
        public int? U_PorcInye { get; set; }

        // ✅ COSTO (precio promedio del artículo en SAP)
        public decimal? AvgPrice { get; set; }

        // Grupo de artículos nativo de SAP (OITM.ItmsGrpCod)
        public int? ItemsGroupCode { get; set; }

        // Stock actual y disponible del artículo en SAP
        public decimal? QuantityOnStock { get; set; }
        public decimal? QuantityAvailable { get; set; }

        // Existencias por almacén (ItemWarehouseInfoCollection)
        public List<AlmacenSapViewmodel> Almacenes { get; set; } = new();

        // Planta a la que pertenece esta fila (TIF / P1). Null = sin almacén identificado.
        public string? Planta { get; set; }


    }

    public class AlmacenSapViewmodel
    {
        public string? WarehouseCode { get; set; }
        public decimal? InStock { get; set; }
        public decimal? Committed { get; set; }
        public decimal? Ordered { get; set; }
        public decimal? Available { get; set; }
    }
}
