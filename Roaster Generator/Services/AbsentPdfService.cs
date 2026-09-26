using System.Globalization;
using System.Text;
using PdfSharp.Fonts;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using PdfSharp.WPFonts;
using Roaster_Generator.Contracts.Absent;

namespace Roaster_Generator.Services;

public sealed class AbsentPdfService(StoreSettingsService storeSettings)
{
    private const string TemplateResourceName =
        "Roaster_Generator.PdfTemplates.absence-notification-roi-fillable.pdf";

    static AbsentPdfService()
    {
        GlobalFontSettings.FallbackFontResolver ??= new AbsencePdfFontResolver();
    }

    public async Task<AbsentPdfDocument> CreateAsync(
        AbsentFormResponse form,
        string? managerName,
        CancellationToken ct)
    {
        var settings = await storeSettings.GetAsync(ct);
        using var template = typeof(AbsentPdfService).Assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException("The embedded absence PDF template is missing.");
        using var document = PdfReader.Open(template, PdfDocumentOpenMode.Modify);

        SetText(document.AcroForm?.Fields, "notification_date",
            form.NotificationDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        SetText(document.AcroForm?.Fields, "store_id", settings.StoreId);
        SetText(document.AcroForm?.Fields, "store_name", settings.StoreName);
        SetText(document.AcroForm?.Fields, "team_member", TeamMember(form));
        SetText(document.AcroForm?.Fields, "reason_for_sickness", form.CancellationReason);
        SetText(document.AcroForm?.Fields, "notification_time", form.NotificationTime);
        SetText(document.AcroForm?.Fields, "notification_method", form.NotificationMethod);
        SetText(document.AcroForm?.Fields, "original_shift", OriginalShift(form.Shift));
        SetText(document.AcroForm?.Fields, "manager_name", managerName?.Trim() ?? string.Empty);

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return new AbsentPdfDocument(output.ToArray(), FileName(form));
    }

    private static void SetText(PdfAcroField.PdfAcroFieldCollection? fields, string name, string value)
    {
        if (fields?[name] is not PdfTextField field)
            throw new InvalidOperationException($"The absence PDF template field '{name}' is missing.");
        field.Text = value;
    }

    private static string TeamMember(AbsentFormResponse form) => string.IsNullOrWhiteSpace(form.PayrollNumber)
        ? form.DriverFullName
        : $"{form.DriverFullName} (Payroll: {form.PayrollNumber.Trim()})";

    private static string OriginalShift(AbsentShiftResponse shift)
    {
        var startDate = shift.Date.AddDays(shift.StartDayOffset);
        var finishDate = shift.Date.AddDays(shift.FinishDayOffset);
        return $"{startDate:dd/MM/yyyy} {shift.StartTime} - {finishDate:dd/MM/yyyy} {shift.FinishTime}";
    }

    private static string FileName(AbsentFormResponse form)
    {
        var requested = $"{form.DriverFullName} {form.Shift.Date:yyyy-MM-dd} absent";
        var cleaned = new StringBuilder(requested.Length);
        const string invalid = "<>:\"/\\|?*";
        foreach (var character in requested)
            cleaned.Append(char.IsControl(character) || invalid.Contains(character) ? ' ' : character);
        var stem = string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.');
        return $"{(stem.Length == 0 ? "absence" : stem)}.pdf";
    }
}

public sealed record AbsentPdfDocument(byte[] Content, string FileName);

internal sealed class AbsencePdfFontResolver : IFontResolver
{
    private const string RegularFace = "absence-pdf-regular";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(RegularFace, mustSimulateBold: false, mustSimulateItalic: isItalic);

    public byte[]? GetFont(string faceName) => faceName == RegularFace ? FontDataHelper.SegoeWP : null;
}
