namespace Plataforma_CG.Models
{
    public class SapBpResponse
    {
        public List<SapBpAddress> BPAddresses { get; set; } = new();
    }

    public class SapBpAddress
    {
        public string AddressName { get; set; }
        public string Street { get; set; }
        public string Block { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string AddressType { get; set; } // bo_ShipTo / bo_BillTo
    }

}
