using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nom.Api.Controllers;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace Nom.Api.Tests.Controllers
{
    /// <summary>
    /// Two actions on one method and route (distinguished only by [Consumes]) make Swashbuckle
    /// throw while building the document, which takes /swagger down for every controller.
    /// </summary>
    public class SwaggerDocumentTests
    {
        [Fact]
        public void The_api_document_builds_for_every_controller()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var environment = Mock.Of<IWebHostEnvironment>(e => e.ApplicationName == "Nom.Api" && e.EnvironmentName == "Development");
            services.AddSingleton(environment);
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
            services.AddControllers().AddApplicationPart(typeof(ShoppingController).Assembly);
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options => options.CustomSchemaIds(type => type.FullName?.Replace('+', '.')));
            using var provider = services.BuildServiceProvider();

            var document = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

            document.Paths.Keys.Should().Contain("/api/Shopping/verification/{ingredientId}/barcode");
            document.Paths.Keys.Should().Contain("/api/Shopping/verification/{ingredientId}/barcode/image");
        }
    }
}
