using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingBuilding.API.Filters
{
    public class DefaultStringSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (schema.Type == "string" && (schema.Default == null || schema.Default is OpenApiString openApiStr && openApiStr.Value == "string"))
            {
                schema.Default = new OpenApiString("");
                schema.Example = new OpenApiString("");
            }

            if (schema.Properties != null)
            {
                foreach (var property in schema.Properties)
                {
                    if (property.Value.Type == "string")
                    {
                        if (property.Value.Default == null || (property.Value.Default is OpenApiString d && d.Value == "string"))
                        {
                            property.Value.Default = new OpenApiString("");
                        }
                        if (property.Value.Example == null || (property.Value.Example is OpenApiString e && e.Value == "string"))
                        {
                            property.Value.Example = new OpenApiString("");
                        }
                    }
                }
            }
        }
    }
}
