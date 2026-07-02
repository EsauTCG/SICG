// ViewModels/SubpedidoOVVm.cs
namespace Plataforma_CG.ViewModels
{
    public class SubpedidoOVVm
    {
        public int Id { get; set; }

        public string? ConsecutivoOV { get; set; }
        public string? SubFolio { get; set; }

        public string? DocumentoSAP { get; set; }
        public string? U_DocMeat { get; set; }

        public string? Almacen { get; set; }
        public decimal TotalPeso { get; set; }
        public decimal TotalImporte { get; set; }
    }

    public class SubpedidoDocMeatUpdateVm
    {
        public int SubpedidoId { get; set; }
        public string? U_DocMeat { get; set; }
    }
}
