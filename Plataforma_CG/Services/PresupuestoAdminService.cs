using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public class PresupuestoAdminService : IPresupuestoAdminService
    {
        private readonly AppDbContext _db;

        public PresupuestoAdminService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<AdminPresupuestoRowDto>> Listar(
            string tipo, int mes, int anio,
            string sku, string canal, int? vendedorId, string cliente)
        {
            tipo = (tipo ?? "").Trim().ToUpperInvariant();
            sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
            canal = string.IsNullOrWhiteSpace(canal) ? null : canal.Trim();
            cliente = string.IsNullOrWhiteSpace(cliente) ? null : cliente.Trim();

            switch (tipo)
            {
                case "CEDIS":
                    {
                        // === AJUSTA NOMBRES SI TU ENTIDAD USA OTROS ===
                        // RowId -> Id (long)
                        // Mes/Anio -> Mes/Anio
                        // Canal -> Canal (o Cedis)
                        // Sku -> ProductoCodigo (o Sku)
                        // Objetivo/Presupuesto/Comentario -> Objetivo/Presupuesto/Comentario

                        var q = _db.PresupuestoCedis.AsNoTracking()
                            .Where(x => x.Mes == mes && x.Anio == anio);

                        if (canal != null) q = q.Where(x => x.Canal.Contains(canal));
                        if (sku != null) q = q.Where(x => x.ProductoCodigo.Contains(sku));

                        var rows = await q
                            .OrderBy(x => x.ProductoCodigo)
                            .Select(x => new AdminPresupuestoRowDto
                            {
                                RowId = x.Id,
                                Tipo = "CEDIS",
                                Mes = x.Mes,
                                Anio = x.Anio,
                                Canal = x.Canal,
                                VendedorId = null,
                                Cliente = null,
                                Sku = x.ProductoCodigo,
                                Objetivo = (int)Math.Round(x.Objetivo),
                                Presupuesto = (int)Math.Round(x.PresupuestoAsignado),
                                Comentario = x.Comentario
                            })
                            .ToListAsync();

                        return rows;
                    }

                case "VENDEDOR":
                    {
                        var q = _db.PresupuestoVendedor.AsNoTracking()
                            .Where(x => x.Mes == mes && x.Anio == anio);

                        if (vendedorId.HasValue) q = q.Where(x => x.VendedorId == vendedorId.Value);
                        if (sku != null) q = q.Where(x => x.ProductoCodigo.Contains(sku));

                        var rows = await q
                            .OrderBy(x => x.VendedorId)
                            .ThenBy(x => x.ProductoCodigo)
                            .Select(x => new AdminPresupuestoRowDto
                            {
                                RowId = x.Id,
                                Tipo = "VENDEDOR",
                                Mes = x.Mes,
                                Anio = x.Anio,
                                Canal = null,
                                VendedorId = x.VendedorId,
                                Cliente = null,
                                Sku = x.ProductoCodigo,
                                Objetivo = (int)Math.Round(x.Objetivo),
                                Presupuesto = (int)Math.Round(x.PresupuestoAsignado),
                                Comentario = x.Comentario
                            })
                            .ToListAsync();

                        return rows;
                    }

                case "CLIENTE":
                    {
                        var q = _db.Presupuestos.AsNoTracking()
                            .Where(x => x.Mes == mes && x.Año == anio);

                        if (cliente != null) q = q.Where(x => x.ClienteId.Contains(cliente));
                        if (sku != null) q = q.Where(x => x.ProductoCodigo.Contains(sku));                      

                        var rows = await q
                            .OrderBy(x => x.ClienteId)
                            .ThenBy(x => x.ProductoCodigo)
                            .Select(x => new AdminPresupuestoRowDto
                            {
                                RowId = x.Id,
                                Tipo = "CLIENTE",
                                Mes = x.Mes,
                                Anio = x.Año,                                
                                VendedorId = null, // si tu tabla trae vendedorId, aquí lo mapeas
                                Cliente = x.ClienteId,
                                Sku = x.ProductoCodigo,
                                Objetivo = (int)Math.Round(x.Objetivo),
                                Presupuesto = (int)Math.Round(x.PresupuestoAsignado),
                                Comentario = x.Comentario
                            })
                            .ToListAsync();

                        return rows;
                    }

                default:
                    return new List<AdminPresupuestoRowDto>();
            }
        }

        public async Task<int> EliminarPorIds(string tipo, List<long> rowIds, string deletedBy, string reason)
        {
            tipo = (tipo ?? "").Trim().ToUpperInvariant();
            if (rowIds == null || rowIds.Count == 0) return 0;

            switch (tipo)
            {
                case "CEDIS":
                    {
                        var rows = await _db.PresupuestoCedis.Where(x => rowIds.Contains(x.Id)).ToListAsync();
                        _db.PresupuestoCedis.RemoveRange(rows);
                        break;
                    }
                case "VENDEDOR":
                    {
                        var rows = await _db.PresupuestoVendedor.Where(x => rowIds.Contains(x.Id)).ToListAsync();
                        _db.PresupuestoVendedor.RemoveRange(rows);
                        break;
                    }
                case "CLIENTE":
                    {
                        var rows = await _db.Presupuestos.Where(x => rowIds.Contains(x.Id)).ToListAsync();
                        _db.Presupuestos.RemoveRange(rows);
                        break;
                    }
                default:
                    return 0;
            }

            return await _db.SaveChangesAsync();
        }

        public async Task<int> EliminarPorFiltro(AdminDeleteByFilterRequest req, string deletedBy)
        {
            req.Tipo = (req.Tipo ?? "").Trim().ToUpperInvariant();

            string sku = string.IsNullOrWhiteSpace(req.Sku) ? null : req.Sku.Trim();
            string canal = string.IsNullOrWhiteSpace(req.Canal) ? null : req.Canal.Trim();
            string cliente = string.IsNullOrWhiteSpace(req.Cliente) ? null : req.Cliente.Trim();

            switch (req.Tipo)
            {
                case "CEDIS":
                    {
                        var q = _db.PresupuestoCedis
                            .Where(x => x.Mes == req.Mes && x.Anio == req.Anio);

                        if (canal != null) q = q.Where(x => x.Canal.Contains(canal));
                        if (sku != null) q = q.Where(x => x.ProductoCodigo.Contains(sku));

                        _db.PresupuestoCedis.RemoveRange(await q.ToListAsync());
                        break;
                    }

                case "VENDEDOR":
                    {
                        var q = _db.PresupuestoVendedor
                            .Where(x => x.Mes == req.Mes && x.Anio == req.Anio);

                        if (req.VendedorId.HasValue) q = q.Where(x => x.VendedorId == req.VendedorId.Value);
                        if (sku != null) q = q.Where(x => x.ProductoCodigo.Contains(sku));

                        _db.PresupuestoVendedor.RemoveRange(await q.ToListAsync());
                        break;
                    }

                case "CLIENTE":
                    {
                        var q = _db.Presupuestos
                            .Where(x => x.Mes == req.Mes && x.Año == req.Anio);

                        if (cliente != null) q = q.Where(x => x.ClienteId.Contains(cliente));                  
                        if (sku != null) q = q.Where(x => x.ProductoCodigo.Contains(sku));

                        _db.Presupuestos.RemoveRange(await q.ToListAsync());
                        break;
                    }

                default:
                    return 0;
            }

            return await _db.SaveChangesAsync();
        }
    }
}
