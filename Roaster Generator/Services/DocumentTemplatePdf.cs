using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

/// <summary>Reads the fields of a fillable PDF form and produces the signed copy.</summary>
public static class DocumentTemplatePdf
{
    private const int RequiredFlag = 2;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static DocumentTemplatePdf()
    {
        GlobalFontSettings.FallbackFontResolver ??= new AbsencePdfFontResolver();
    }

    /// <exception cref="InvalidDataException">The file is not a fillable PDF.</exception>
    public static IReadOnlyList<DocumentTemplateField> ReadFields(byte[] content)
    {
        using var document = Open(content, PdfDocumentOpenMode.Import);
        var fields = FieldsOf(document);
        EnsurePagesCanBeDrawnOn(document);
        var usable = fields is null
            ? []
            : fields.Names.Select(name => (Name: name, Field: fields[name])).Where(item => IsUsable(item.Field)).ToList();
        var anyMarkedRequired = usable.Any(item => IsMarkedRequired(item.Field!));
        var found = usable.Select(item => Guess(item.Name, item.Field!, anyMarkedRequired)).ToList();
        if (found.Count == 0)
            throw new InvalidDataException("This PDF has no fillable fields. Upload a fillable PDF form.");
        return found;
    }

    /// <summary>Fills the form, places the signature and locks every field.</summary>
    /// <param name="values">Text by PDF field name; checkboxes use "true".</param>
    public static byte[] Fill(
        byte[] template, IReadOnlyList<DocumentTemplateField> mapping, IReadOnlyDictionary<string, string?> values, byte[] signaturePng)
    {
        using var document = Open(template, PdfDocumentOpenMode.Modify);
        var fields = FieldsOf(document) ?? throw new InvalidDataException("The form template has no fields.");
        var signed = false;
        foreach (var item in mapping)
        {
            if (fields[item.Name] is not { } field) continue;
            if (item.Source == DocumentFieldSource.Signature)
            {
                DrawSignature(document, field, signaturePng);
                signed = true;
            }
            else if (values.TryGetValue(item.Name, out var value) && !string.IsNullOrEmpty(value))
            {
                if (field is PdfTextField text) text.Text = value;
                else if (field is PdfCheckBoxField checkbox) checkbox.Checked = value == "true";
            }
        }

        if (!signed) throw new InvalidDataException("The form template has no signature field.");

        // Manager fields stay open until the manager signs.
        var managerFields = mapping.Where(item => IsManagerSource(item.Source)).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in fields.Names.Where(name => !managerFields.Contains(name)))
            if (fields[name] is { } field) field.ReadOnly = true;

        return Save(document);
    }

    /// <summary>Completes a form the employee has signed with the manager's details and signature, then locks it.</summary>
    public static byte[] FillManagerPart(
        byte[] signedForm, IReadOnlyList<DocumentTemplateField> mapping, IReadOnlyDictionary<string, string?> values, byte[] signaturePng)
    {
        using var document = Open(signedForm, PdfDocumentOpenMode.Modify);
        var fields = FieldsOf(document) ?? throw new InvalidDataException("The form has no fields.");
        var signed = false;
        foreach (var item in mapping.Where(item => IsManagerSource(item.Source)))
        {
            if (fields[item.Name] is not { } field) continue;
            if (item.Source == DocumentFieldSource.ManagerSignature)
            {
                DrawSignature(document, field, signaturePng);
                signed = true;
            }
            else if (field is PdfTextField text && values.TryGetValue(item.Name, out var value) && !string.IsNullOrEmpty(value))
            {
                text.Text = value;
            }
        }

        if (!signed) throw new InvalidDataException("The form has no manager signature field.");
        foreach (var name in fields.Names)
            if (fields[name] is { } field) field.ReadOnly = true;

        return Save(document);
    }

    public static bool IsManagerSource(DocumentFieldSource source) =>
        source is DocumentFieldSource.ManagerName or DocumentFieldSource.ManagerSignature or DocumentFieldSource.ManagerDate;

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    public static bool IsPng(ReadOnlySpan<byte> content) => content.StartsWith(PngSignature);

    /// <summary>Some PDFs compress pages in a way PDFsharp can't read, which would only fail when an employee signs.</summary>
    private static void EnsurePagesCanBeDrawnOn(PdfDocument document)
    {
        try
        {
            foreach (var page in document.Pages.Cast<PdfPage>()) _ = page.Contents;
        }
        catch (InvalidOperationException)
        {
            throw new InvalidDataException("This PDF can't be used. Open it, save it again as a PDF, and upload that copy.");
        }
    }

    private static PdfAcroField.PdfAcroFieldCollection? FieldsOf(PdfDocument document)
    {
        try
        {
            return document.AcroForm?.Fields;
        }
        catch (InvalidOperationException)
        {
            // PDFsharp reports a document with no form this way.
            return null;
        }
    }

    private static bool IsUsable(PdfAcroField? field) => field is PdfTextField or PdfCheckBoxField or PdfSignatureField;

    private static PdfDocument Open(byte[] content, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(new MemoryStream(content), mode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("This PDF could not be read.");
        }
    }

    private static void DrawSignature(PdfDocument document, PdfAcroField field, byte[] png)
    {
        var rectangle = field.Elements.GetRectangle(PdfAnnotation.Keys.Rect);
        var pageReference = field.Elements.GetReference("/P");
        var page = document.Pages.Cast<PdfPage>().FirstOrDefault(item => item.Reference == pageReference)
            ?? document.Pages.Cast<PdfPage>().FirstOrDefault(page => page.Annotations.Cast<PdfAnnotation>()
                .Any(annotation => ReferenceEquals(annotation.Elements, field.Elements)))
            ?? document.Pages[0];

        // The field's own box is drawn above page content, so it would cover the signature.
        foreach (var annotation in page.Annotations.Cast<PdfAnnotation>().ToArray())
            if (annotation.Reference is not null && annotation.Reference == field.Reference)
                page.Annotations.Remove(annotation);

        using var stream = new MemoryStream(png);
        using var image = XImage.FromStream(stream);
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var scale = Math.Min(rectangle.Width / image.PointWidth, rectangle.Height / image.PointHeight);
        var width = image.PointWidth * scale;
        var height = image.PointHeight * scale;
        graphics.DrawImage(image, rectangle.X1, page.Height.Point - rectangle.Y2 + (rectangle.Height - height) / 2, width, height);
    }

    /// <summary>
    /// A starting point the administrator can correct, based on the field's name. A form can also
    /// set a field's tooltip for the label, and mark fields required; with none marked, text fields are.
    /// </summary>
    private static DocumentTemplateField Guess(string name, PdfAcroField field, bool anyMarkedRequired)
    {
        var isCheckbox = field is PdfCheckBoxField;
        var tooltip = field.Elements.GetString("/TU");
        var words = string.Join(' ', name.Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries));
        var label = !string.IsNullOrWhiteSpace(tooltip) ? tooltip
            : words.Length == 0 ? name
            : char.ToUpperInvariant(words[0]) + words[1..];
        return new DocumentTemplateField
        {
            Name = name,
            Label = label,
            IsCheckbox = isCheckbox,
            Source = isCheckbox ? GuessCheckboxSource(name.ToLowerInvariant()) : GuessSource(name.ToLowerInvariant()),
            Required = !isCheckbox && (!anyMarkedRequired || IsMarkedRequired(field))
        };
    }

    /// <summary>Cover boxes aren't the employee's choice: OBU always applies, and moped cover follows the vehicle.</summary>
    private static DocumentFieldSource GuessCheckboxSource(string name) => name switch
    {
        "cover_obu_yes" => DocumentFieldSource.AlwaysTicked,
        "cover_moped_yes" => DocumentFieldSource.TickedForMoped,
        _ when name.StartsWith("cover_", StringComparison.Ordinal) => DocumentFieldSource.Ignore,
        _ => DocumentFieldSource.Ask
    };

    private static DocumentFieldSource GuessSource(string name) => name switch
    {
        _ when name.Contains("franchisee") => DocumentFieldSource.FranchiseeName,
        _ when name.Contains("cover_start") => DocumentFieldSource.Today,
        _ when name.StartsWith("manager_", StringComparison.Ordinal) && name.Contains("date") => DocumentFieldSource.ManagerDate,
        _ when name.StartsWith("manager_", StringComparison.Ordinal) && name.Contains("sign") => DocumentFieldSource.ManagerSignature,
        _ when name.StartsWith("manager_", StringComparison.Ordinal) && name.Contains("name") => DocumentFieldSource.ManagerName,
        _ when name.StartsWith("reference", StringComparison.Ordinal) || name.StartsWith("job", StringComparison.Ordinal) => DocumentFieldSource.Ask,
        _ when name.Contains("sign") && !name.Contains("date") => DocumentFieldSource.Signature,
        _ when name.Contains("date") => DocumentFieldSource.Today,
        _ when name.Contains("registration") => DocumentFieldSource.VehicleRegistration,
        _ when name.Contains("point") => DocumentFieldSource.PenaltyPoints,
        _ when name.Contains("phone") || name.Contains("mobile") || name.Contains("contact_no") => DocumentFieldSource.PhoneNumber,
        _ when name.Contains("payroll") => DocumentFieldSource.PayrollNumber,
        _ when name.Contains("store") && name.Contains("name") => DocumentFieldSource.StoreName,
        _ when name.Contains("store") => DocumentFieldSource.StoreId,
        _ when name.Contains("number") && name.Contains("employee") => DocumentFieldSource.EmployeeNumber,
        _ when name.Contains("surname") => DocumentFieldSource.LastName,
        _ when name.Contains("forename") => DocumentFieldSource.FirstName,
        _ when name.Contains("name") => DocumentFieldSource.FullName,
        _ => DocumentFieldSource.Ask
    };

    private static bool IsMarkedRequired(PdfAcroField field) => (field.Elements.GetInteger("/Ff") & RequiredFlag) != 0;
}
