using System;
using System.Collections.Generic;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace Nom.Orch.UtilityServices
{
    public sealed record RecipePdfExport(DateTime ExportDate, int RecipeCount, IReadOnlyList<RecipePdfRecipe> Recipes);

    public sealed record RecipePdfRecipe(
        string Name,
        string? Description,
        IReadOnlyList<string> Times,
        IReadOnlyList<string>? IngredientLines,
        IReadOnlyList<string>? Steps);

    /// <summary>
    /// Lays out the recipe export as a US Letter PDF with MigraDoc: a running "Recipe Export" header,
    /// each recipe's name, description, times, ingredients and numbered steps, and a "Page X of Y" footer.
    /// </summary>
    public static class RecipePdfRenderer
    {
        private static readonly Color HeaderBlue = Color.FromRgb(0x15, 0x65, 0xC0);
        private static readonly Color MutedGrey = Color.FromRgb(0x75, 0x75, 0x75);
        private static readonly Color RuleGrey = Color.FromRgb(0xE0, 0xE0, 0xE0);

        public static byte[] Render(RecipePdfExport export)
        {
            PdfFontResolver.EnsureInstalled();

            var document = new Document();
            document.Info.Title = "Recipe Export";
            var normal = document.Styles[StyleNames.Normal]!;
            normal.Font.Name = PdfFontResolver.Family;
            normal.Font.Size = 11;

            var section = document.AddSection();
            var setup = section.PageSetup;
            setup.PageWidth = Unit.FromInch(8.5);
            setup.PageHeight = Unit.FromInch(11);
            setup.LeftMargin = setup.RightMargin = Unit.FromInch(1);
            setup.HeaderDistance = setup.FooterDistance = Unit.FromInch(1);
            setup.TopMargin = Unit.FromInch(1) + Unit.FromPoint(24);
            setup.BottomMargin = Unit.FromInch(1) + Unit.FromPoint(16);

            var header = section.Headers.Primary.AddParagraph("Recipe Export");
            header.Format.Font.Size = 20;
            header.Format.Font.Bold = true;
            header.Format.Font.Color = HeaderBlue;

            var footer = section.Footers.Primary.AddParagraph();
            footer.Format.Alignment = ParagraphAlignment.Center;
            footer.AddText("Page ");
            footer.AddPageField();
            footer.AddText(" of ");
            footer.AddNumPagesField();

            Muted(section.AddParagraph($"Exported: {export.ExportDate:yyyy-MM-dd}"));
            Muted(section.AddParagraph($"{export.RecipeCount} recipe(s)"));

            var spaceBefore = 16 + 8;
            foreach (var recipe in export.Recipes)
            {
                AddRecipe(section, recipe, spaceBefore);
                spaceBefore = 8;
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            using var stream = new MemoryStream();
            renderer.PdfDocument.Save(stream, false);
            return stream.ToArray();
        }

        private static void AddRecipe(Section section, RecipePdfRecipe recipe, double spaceBefore)
        {
            var name = section.AddParagraph(recipe.Name);
            name.Format.SpaceBefore = Unit.FromPoint(spaceBefore);
            name.Format.Font.Size = 16;
            name.Format.Font.Bold = true;

            if (!string.IsNullOrEmpty(recipe.Description))
            {
                var description = section.AddParagraph(recipe.Description);
                description.Format.SpaceBefore = Unit.FromPoint(4);
                description.Format.Font.Size = 10;
                description.Format.Font.Italic = true;
            }

            if (recipe.Times.Count > 0)
            {
                var times = Muted(section.AddParagraph(string.Join(" | ", recipe.Times)));
                times.Format.SpaceBefore = Unit.FromPoint(4);
            }

            if (recipe.IngredientLines != null)
            {
                Heading(section, "Ingredients");
                foreach (var line in recipe.IngredientLines)
                    ListItem(section, $"• {line}");
            }

            if (recipe.Steps != null)
            {
                Heading(section, "Instructions");
                var stepNum = 1;
                foreach (var step in recipe.Steps)
                    ListItem(section, $"{stepNum++}. {step}");
            }

            var rule = section.AddParagraph();
            rule.Format.SpaceBefore = Unit.FromPoint(10);
            rule.Format.Font.Size = 1;
            rule.Format.LineSpacingRule = LineSpacingRule.Exactly;
            rule.Format.LineSpacing = Unit.FromPoint(1);
            rule.Format.Borders.Bottom.Width = Unit.FromPoint(1);
            rule.Format.Borders.Bottom.Color = RuleGrey;
        }

        private static void Heading(Section section, string text)
        {
            var paragraph = section.AddParagraph(text);
            paragraph.Format.SpaceBefore = Unit.FromPoint(8);
            paragraph.Format.Font.Name = PdfFontResolver.SemiboldFamily;
            paragraph.Format.Font.Size = 12;
        }

        private static void ListItem(Section section, string text)
        {
            var paragraph = section.AddParagraph(text);
            paragraph.Format.LeftIndent = Unit.FromPoint(12);
            paragraph.Format.Font.Size = 10;
        }

        private static Paragraph Muted(Paragraph paragraph)
        {
            paragraph.Format.Font.Size = 9;
            paragraph.Format.Font.Color = MutedGrey;
            return paragraph;
        }
    }
}
