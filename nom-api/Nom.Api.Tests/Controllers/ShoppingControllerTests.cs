using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nom.Api.Controllers;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Xunit;

namespace Nom.Api.Tests.Controllers
{
    /// <summary>
    /// Every shopping action is scoped by the caller's HouseholdMember claims: list and create
    /// endpoints check the household they are given, and endpoints that address an existing
    /// row check the row's own household and ignore anything the body says.
    /// </summary>
    public class ShoppingControllerTests
    {
        private const long Person = 10;
        private const long Mine = 100;
        private const long Theirs = 200;

        private readonly Mock<IHouseholdShoppingService> _shopping = new();
        private readonly Mock<IIngredientVerificationService> _verifications = new();

        private ShoppingController Controller(params long[] households)
        {
            var claims = new List<Claim> { new("PersonId", Person.ToString()) };
            claims.AddRange(households.Select(h => new Claim("HouseholdMember", h.ToString())));
            return new ShoppingController(_shopping.Object, _verifications.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
                },
            };
        }

        private static IFormFile Jpeg(string name = "label.jpg", string contentType = "image/jpeg")
        {
            var bytes = Encoding.ASCII.GetBytes("not really a jpeg but the controller does not decode");
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", name) { Headers = new HeaderDictionary(), ContentType = contentType };
        }

        [Fact]
        public async Task GetState_ForbidsNonMembers_AndServesMembers()
        {
            _shopping.Setup(s => s.GetStateAsync(Mine)).ReturnsAsync(new ShoppingStateModel());
            var controller = Controller(Mine);

            (await controller.GetState(Theirs)).Result.Should().BeOfType<ForbidResult>();
            (await controller.GetState(Mine)).Result.Should().BeOfType<OkObjectResult>();
            _shopping.Verify(s => s.GetStateAsync(Theirs), Times.Never);
        }

        [Fact]
        public async Task AddItem_UsesTheBodyHousehold_AndForbidsOutsiders()
        {
            var controller = Controller(Mine);
            var model = new HouseholdShoppingItemCreateModel { HouseholdId = Theirs, Name = "Milk" };

            (await controller.AddItem(model)).Result.Should().BeOfType<ForbidResult>();
            _shopping.Verify(s => s.AddItemAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<HouseholdShoppingItemCreateModel>()), Times.Never);

            _shopping.Setup(s => s.AddItemAsync(Mine, Person, It.IsAny<HouseholdShoppingItemCreateModel>()))
                .ReturnsAsync(new ShoppingItemAddResult { Item = new HouseholdShoppingItemModel { Id = 5, HouseholdId = Mine }, Created = true });
            model.HouseholdId = Mine;
            (await controller.AddItem(model)).Result.Should().BeOfType<CreatedAtActionResult>();

            _shopping.Setup(s => s.AddItemAsync(Mine, Person, It.IsAny<HouseholdShoppingItemCreateModel>()))
                .ReturnsAsync(new ShoppingItemAddResult { Item = new HouseholdShoppingItemModel { Id = 5, HouseholdId = Mine }, Created = false });
            (await controller.AddItem(model)).Result.Should().BeOfType<OkObjectResult>();

            _shopping.Setup(s => s.AddItemAsync(Mine, Person, It.IsAny<HouseholdShoppingItemCreateModel>())).ReturnsAsync((ShoppingItemAddResult?)null);
            (await controller.AddItem(model)).Result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task ItemMutations_CheckTheRowsHousehold_NotTheCallers_Claim()
        {
            _shopping.Setup(s => s.GetItemAsync(1)).ReturnsAsync(new HouseholdShoppingItemModel { Id = 1, HouseholdId = Theirs });
            _shopping.Setup(s => s.GetItemAsync(2)).ReturnsAsync((HouseholdShoppingItemModel?)null);
            var controller = Controller(Mine);

            (await controller.GetItem(1)).Result.Should().BeOfType<ForbidResult>();
            (await controller.UpdateItem(1, new HouseholdShoppingItemUpdateModel { Name = "x" })).Result.Should().BeOfType<ForbidResult>();
            (await controller.DeleteItem(1)).Should().BeOfType<ForbidResult>();
            (await controller.UpdateItem(2, new HouseholdShoppingItemUpdateModel())).Result.Should().BeOfType<NotFoundResult>();
            (await controller.DeleteItem(2)).Should().BeOfType<NotFoundResult>();

            _shopping.Verify(s => s.UpdateItemAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<HouseholdShoppingItemUpdateModel>()), Times.Never);
            _shopping.Verify(s => s.DeleteItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task Checks_Skip_Measurements_AndBarcode_ForbidOtherHouseholds()
        {
            var controller = Controller(Mine);

            (await controller.SetCheck(new ShoppingCheckRequestModel { HouseholdId = Theirs, ItemKey = "1", Checked = true })).Result.Should().BeOfType<ForbidResult>();
            (await controller.ClearChecks(new HouseholdRequestModel { HouseholdId = Theirs })).Should().BeOfType<ForbidResult>();
            (await controller.Skip(7, new HouseholdRequestModel { HouseholdId = Theirs })).Should().BeOfType<ForbidResult>();
            (await controller.RecordMeasurements(7, new IngredientMeasurementsModel { HouseholdId = Theirs, CupGrams = 80 })).Result.Should().BeOfType<ForbidResult>();
            (await controller.RecordBarcode(7, new BarcodeRequestModel { HouseholdId = Theirs, Upc = "036000291452" })).Result.Should().BeOfType<ForbidResult>();
            (await controller.NeedsVerification(Theirs, "1,2")).Result.Should().BeOfType<ForbidResult>();
            (await controller.ListVerifications(Theirs, 7)).Result.Should().BeOfType<ForbidResult>();

            _shopping.VerifyNoOtherCalls();
            _verifications.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Checks_MapServiceOutcomesToStatusCodes()
        {
            var controller = Controller(Mine);
            _shopping.Setup(s => s.SetCheckAsync(Mine, Person, It.IsAny<ShoppingCheckRequestModel>()))
                .ThrowsAsync(new ArgumentException("Send exactly one of itemKey or itemId."));
            (await controller.SetCheck(new ShoppingCheckRequestModel { HouseholdId = Mine, Checked = true })).Result.Should().BeOfType<BadRequestObjectResult>();

            _shopping.Setup(s => s.SetCheckAsync(Mine, Person, It.IsAny<ShoppingCheckRequestModel>())).ReturnsAsync((ShoppingCheckResultModel?)null);
            (await controller.SetCheck(new ShoppingCheckRequestModel { HouseholdId = Mine, ItemId = 9, Checked = true })).Result.Should().BeOfType<NotFoundObjectResult>();

            _shopping.Setup(s => s.SetCheckAsync(Mine, Person, It.IsAny<ShoppingCheckRequestModel>())).ReturnsAsync(new ShoppingCheckResultModel { ItemKey = "1", Checked = true });
            (await controller.SetCheck(new ShoppingCheckRequestModel { HouseholdId = Mine, ItemKey = "1", Checked = true })).Result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task VerificationRows_AreGuardedByTheirOwnHousehold()
        {
            _verifications.Setup(v => v.GetAsync(1)).ReturnsAsync(new IngredientVerificationModel { Id = 1, HouseholdId = Theirs });
            _verifications.Setup(v => v.GetAsync(2)).ReturnsAsync(new IngredientVerificationModel { Id = 2, HouseholdId = null });
            _verifications.Setup(v => v.GetAsync(3)).ReturnsAsync((IngredientVerificationModel?)null);
            _verifications.Setup(v => v.GetAsync(4)).ReturnsAsync(new IngredientVerificationModel { Id = 4, HouseholdId = Mine });
            _verifications.Setup(v => v.ConfirmAsync(4, Person, It.IsAny<IngredientVerificationConfirmModel>()))
                .ThrowsAsync(new InvalidOperationException("closed"));
            _verifications.Setup(v => v.GetImageAsync(4)).ReturnsAsync((new byte[] { 0xFF, 0xD8 }, "image/jpeg"));
            var controller = Controller(Mine);

            (await controller.Confirm(1, new IngredientVerificationConfirmModel())).Result.Should().BeOfType<ForbidResult>();
            (await controller.Confirm(2, new IngredientVerificationConfirmModel())).Result.Should().BeOfType<ForbidResult>();
            (await controller.Confirm(3, new IngredientVerificationConfirmModel())).Result.Should().BeOfType<NotFoundResult>();
            (await controller.Confirm(4, new IngredientVerificationConfirmModel())).Result.Should().BeOfType<ConflictObjectResult>();
            (await controller.GetVerificationImage(1)).Should().BeOfType<ForbidResult>();
            (await controller.GetVerificationImage(3)).Should().BeOfType<NotFoundResult>();
            (await controller.GetVerificationImage(4)).Should().BeOfType<FileContentResult>();

            _verifications.Verify(v => v.ConfirmAsync(It.IsIn(1L, 2L, 3L), It.IsAny<long>(), It.IsAny<IngredientVerificationConfirmModel>()), Times.Never);
        }

        [Fact]
        public async Task LabelUpload_ChecksHouseholdAndContentType_AndMapsRateLimiting()
        {
            var controller = Controller(Mine);

            (await controller.ScanLabel(7, Theirs, Jpeg())).Result.Should().BeOfType<ForbidResult>();
            (await controller.ScanLabel(7, Mine, Jpeg("label.gif", "image/gif"))).Result.Should().BeOfType<BadRequestObjectResult>();
            (await controller.RecordBarcodeFromImage(7, Mine, Jpeg("code.bmp", "image/bmp"))).Result.Should().BeOfType<BadRequestObjectResult>();
            _verifications.VerifyNoOtherCalls();

            _verifications.Setup(v => v.ScanLabelAsync(7, Mine, Person, It.IsAny<byte[]>()))
                .ReturnsAsync(new LabelScanResultModel { VerificationId = 11 });
            (await controller.ScanLabel(7, Mine, Jpeg())).Result.Should().BeOfType<OkObjectResult>();

            _verifications.Setup(v => v.ScanLabelAsync(7, Mine, Person, It.IsAny<byte[]>()))
                .ThrowsAsync(new InvalidOperationException("Too many label scans this hour; try again later."));
            var limited = (await controller.ScanLabel(7, Mine, Jpeg())).Result.Should().BeOfType<ObjectResult>().Subject;
            limited.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);

            _verifications.Setup(v => v.ScanLabelAsync(7, Mine, Person, It.IsAny<byte[]>()))
                .ThrowsAsync(new ArgumentException("The file is not an image that can be read."));
            (await controller.ScanLabel(7, Mine, Jpeg())).Result.Should().BeOfType<BadRequestObjectResult>();

            _verifications.Setup(v => v.ScanLabelAsync(7, Mine, Person, It.IsAny<byte[]>())).ReturnsAsync((LabelScanResultModel?)null);
            (await controller.ScanLabel(7, Mine, Jpeg())).Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task NeedsVerification_ParsesTheIdList_AndCapsIt()
        {
            var controller = Controller(Mine);
            _verifications.Setup(v => v.NeedsVerificationAsync(Mine, It.IsAny<IReadOnlyCollection<long>>()))
                .ReturnsAsync(new List<NeedsVerificationModel>());

            (await controller.NeedsVerification(Mine, " 1, 2,x,2 ,0")).Result.Should().BeOfType<OkObjectResult>();
            _verifications.Verify(v => v.NeedsVerificationAsync(Mine, It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 }))), Times.Once);

            var tooMany = string.Join(',', Enumerable.Range(1, 201));
            (await controller.NeedsVerification(Mine, tooMany)).Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Barcode_InvalidGtin_IsABadRequest()
        {
            var controller = Controller(Mine);
            _verifications.Setup(v => v.RecordBarcodeAsync(7, Mine, Person, "036000291453"))
                .ThrowsAsync(new ArgumentException("That is not a valid GTIN-8/12/13/14 barcode."));

            (await controller.RecordBarcode(7, new BarcodeRequestModel { HouseholdId = Mine, Upc = "036000291453" })).Result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
