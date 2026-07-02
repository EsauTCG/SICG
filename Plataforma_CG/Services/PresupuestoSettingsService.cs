using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Data.Entities;
using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public class PresupuestoSettingsService : IPresupuestoSettingsService
    {
        private const string KEY = "Presupuesto.Modo";
        private readonly AppDbContext _db;

        public PresupuestoSettingsService(AppDbContext db) => _db = db;

        public async Task<PresupuestoModo> GetModoAsync()
        {
            var row = await _db.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == KEY);

            if (row == null) return PresupuestoModo.VENDEDOR;

            return Enum.TryParse<PresupuestoModo>(row.Value, true, out var modo)
                ? modo
                : PresupuestoModo.VENDEDOR;
        }

        public async Task<PresupuestoModo> SetModoAsync(PresupuestoModo modo, string? updatedBy)
        {
            var row = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == KEY);

            if (row == null)
            {
                row = new AppSetting { Key = KEY };
                _db.AppSettings.Add(row);
            }

            row.Value = modo.ToString();
            row.UpdatedBy = updatedBy;
            row.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return modo;
        }
    }
}
