namespace Plataforma_CG.ViewModels
{
    public class CatalogoProveedorSapViewModel
    {
        public string? CardCode { get; set; }
        public string? CardName { get; set; }
        public string? CardForeignName { get; set; }
        public string? FederalTaxID { get; set; }
        public string? Phone1 { get; set; }
        public string? Cellular { get; set; }
        public string? EmailAddress { get; set; }
        public string? Currency { get; set; }
        public int? GroupCode { get; set; }
        public string? GroupName { get; set; }
        public int? PayTermsGrpCode { get; set; }
        public string? PaymentTermsName { get; set; }
        public decimal CurrentAccountBalance { get; set; }
        public string? Address { get; set; }
        public string? ZipCode { get; set; }
        public string? City { get; set; }
        public string? County { get; set; }
        public string? Country { get; set; }
        public bool Active { get; set; }
        public bool Frozen { get; set; }
    }
}
