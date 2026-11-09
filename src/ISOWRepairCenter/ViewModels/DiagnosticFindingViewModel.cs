using ISOWRepairCenter.Core;

namespace ISOWRepairCenter.ViewModels;

public sealed class DiagnosticFindingViewModel : ObservableObject
{
    private string _language;
    public DiagnosticFinding Finding { get; }
    public string Summary => TurkishTranslation?.Summary ?? Finding.Summary;
    public string Evidence => TurkishTranslation?.Evidence ?? Finding.Evidence;
    public string Recommendation => TurkishTranslation?.Recommendation ?? Finding.Recommendation;
    public FindingSeverity Severity => Finding.Severity;

    public DiagnosticFindingViewModel(DiagnosticFinding finding, string language) { Finding = finding; _language = language; }
    public void SetLanguage(string language) { _language = language; OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(Recommendation)); }

    private Translation? TurkishTranslation => _language == "tr" && Turkish.TryGetValue(Finding.Code, out var text) ? text : null;

    private static readonly Dictionary<string, Translation> Turkish = new()
    {
        ["file-system-clean"] = new("Sistem birimi onarım için işaretli değil.", "fsutil, birimin temiz olduğunu bildirdi.", "Bu kontrole göre dosya sistemi onarımı gerekmiyor."),
        ["file-system-dirty"] = new("Sistem birimi onarım için işaretli.", "fsutil, birimin onarım için işaretli olduğunu bildirdi.", "Önemli dosyaları yedekleyin ve onarım planlamadan önce dosya sistemi hatalarını inceleyin."),
        ["component-store-healthy"] = new("Windows bileşen deposu sağlıklı.", "DISM, bileşen deposunda bozulma bulmadı.", "Bu kontrole göre DISM onarımı gerekmiyor."),
        ["system-files-healthy"] = new("Korunan sistem dosyaları doğrulamayı geçti.", "SFC bütünlük ihlali bulmadı.", "Bu kontrole göre SFC onarımı gerekmiyor."),
        ["system-files-warning"] = new("SFC, sistem dosyası bütünlüğü sorunu bildirdi.", "SFC doğrulama çıktısında bütünlük ihlali var.", "Oturum raporunu inceledikten sonra yönlendirmeli DISM ve SFC onarımını çalıştırın."),
        ["disk-health-warning"] = new("Bir fiziksel disk uyarı veya sağlıksız durum bildirdi.", "Get-PhysicalDisk çıktısında Warning veya Unhealthy var.", "Verileri yedekleyin ve üreticinin disk tanı aracını çalıştırın."),
        ["disk-health-ok"] = new("Fiziksel disk sağlığı uyarı bildirmedi.", "Get-PhysicalDisk Warning veya Unhealthy bildirmedi.", "Disk sağlığını izlemeye ve yedek almaya devam edin."),
        ["update-service-warning"] = new("En az bir Windows Update servisi durdurulmuş.", "Windows Update servis durumunda Stopped görünüyor.", "Bileşenleri sıfırlamadan önce servis yapılandırmasını ve güncelleme olaylarını inceleyin."),
        ["proxy-direct"] = new("WinHTTP doğrudan erişim için yapılandırılmış.", "netsh, proxy sunucusu olmadan doğrudan erişim bildirdi.", "Bu kontrole göre proxy sıfırlama gerekmiyor."),
        ["diagnostic-command-failed"] = new("Bir tanılama komutu tamamlanamadı.", "Tanılama komutu başarıyla bitmedi.", "Onarım çalıştırmadan önce kaydedilmiş komut çıktısını inceleyin.")
    };

    private sealed record Translation(string Summary, string Evidence, string Recommendation);
}
