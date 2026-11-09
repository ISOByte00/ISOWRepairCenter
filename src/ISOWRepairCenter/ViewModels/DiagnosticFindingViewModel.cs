using ISOWRepairCenter.Core;

namespace ISOWRepairCenter.ViewModels;

public sealed class DiagnosticFindingViewModel : ObservableObject
{
    private string _language;
    public DiagnosticFinding Finding { get; }
    public string Summary => _language == "tr" && Turkish.TryGetValue(Finding.Code, out var text) ? text : Finding.Summary;
    public string Evidence => Finding.Evidence;
    public string Recommendation => Finding.Recommendation;
    public FindingSeverity Severity => Finding.Severity;

    public DiagnosticFindingViewModel(DiagnosticFinding finding, string language) { Finding = finding; _language = language; }
    public void SetLanguage(string language) { _language = language; OnPropertyChanged(nameof(Summary)); }

    private static readonly Dictionary<string, string> Turkish = new()
    {
        ["file-system-clean"] = "Sistem birimi onarım için işaretli değil.",
        ["file-system-dirty"] = "Sistem birimi onarım için işaretli.",
        ["component-store-healthy"] = "Windows bileşen deposu sağlıklı.",
        ["system-files-healthy"] = "Korunan sistem dosyaları doğrulamayı geçti.",
        ["system-files-warning"] = "SFC, sistem dosyası bütünlüğü sorunu bildirdi.",
        ["disk-health-warning"] = "Bir fiziksel disk uyarı veya sağlıksız durum bildirdi.",
        ["disk-health-ok"] = "Fiziksel disk sağlığı uyarı bildirmedi.",
        ["update-service-warning"] = "En az bir Windows Update servisi durdurulmuş.",
        ["proxy-direct"] = "WinHTTP doğrudan erişim için yapılandırılmış.",
        ["diagnostic-command-failed"] = "Bir tanılama komutu tamamlanamadı."
    };
}
