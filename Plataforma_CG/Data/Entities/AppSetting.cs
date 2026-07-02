namespace Plataforma_CG.Data.Entities
{
    public class AppSetting
    {
        public string Key { get; set; } = default!;
        public string Value { get; set; } = default!;
        public DateTime UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
        public byte[] RowVer { get; set; } = default!;
    }
}
