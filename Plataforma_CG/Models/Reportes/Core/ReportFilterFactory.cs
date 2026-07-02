using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;


namespace Plataforma_CG.Models.Reportes.Core
{
    public class ReportFilterFactory : IReportFilterFactory
    {

        public IReportFilter Create(
            IReportDefinition report,
            IQueryCollection query)
        {
            var instance =
                Activator.CreateInstance(report.FilterType);

            if (instance is not IReportFilter filter)
            {
                throw new InvalidOperationException(
                    $"{report.FilterType.Name} no implementa IReportFilter");
            }

            BindProperties(instance, query);

            return filter;
        }

        private static void BindProperties(
            object target,
            IQueryCollection query)
        {
            var properties = target.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanWrite);


            foreach (var property in properties)
            {
                if (!query.TryGetValue(property.Name, out var value))
                    continue;

                if (String.IsNullOrWhiteSpace(value))
                    continue;

                var converted =
                    ConvertValue(
                        value!,
                        property.PropertyType);

                property.SetValue(target, converted);
            }
        }

        private static object? ConvertValue(
            string value,
            Type propertyType)
        {
            var targetType =
                Nullable.GetUnderlyingType(propertyType)
                ?? propertyType;

            if (targetType == typeof(string))
                return value;

            if (targetType == typeof(int))
                return int.Parse(value);

            if (targetType == typeof(DateTime))
                return DateTime.Parse(value);

            return Convert.ChangeType(
                value,
                targetType);
        }
    }
}
