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
    public string SeverityLabel => _language == "tr" ? Severity switch { FindingSeverity.Critical => "Kritik", FindingSeverity.Warning => "Uyarı", _ => "Bilgi" } : Severity.ToString();
    public string ConfidenceLabel => _language == "tr" ? Finding.Confidence switch { FindingConfidence.Known => "Doğrulandı", FindingConfidence.Unknown => "Bilinmiyor", _ => "Manuel inceleme gerekli" } : Finding.Confidence switch { FindingConfidence.Known => "Known", FindingConfidence.Unknown => "Unknown", _ => "Manual review required" };
    public string OperationTitle => _language == "tr" && TurkishOperationTitles.TryGetValue(Finding.OperationId, out var title) ? title : Finding.OperationTitle;

    public DiagnosticFindingViewModel(DiagnosticFinding finding, string language) { Finding = finding; _language = language; }
    public void SetLanguage(string language) { _language = language; OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(Recommendation)); OnPropertyChanged(nameof(SeverityLabel)); OnPropertyChanged(nameof(ConfidenceLabel)); OnPropertyChanged(nameof(OperationTitle)); }

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
        ["diagnostic-command-failed"] = new("Bir tanılama komutu tamamlanamadı.", "Tanılama komutu başarıyla bitmedi.", "Onarım çalıştırmadan önce kaydedilmiş komut çıktısını inceleyin."),
        ["diagnostic-cancelled"] = new("Tanılama bir sonuca ulaşmadan iptal edildi.", "İşlem iptal edildi.", "Sistem boşta olduğunda tanılamayı yeniden çalıştırın."),
        ["diagnostic-output-missing"] = new("Tanılama okunabilir çıktı üretmeden tamamlandı.", "Komut çıktısı boş veya okunamadı.", "Onarım kararı vermeden önce kaydedilmiş çıktıyı manuel inceleyin."),
        ["diagnostic-manual-review"] = new("Tanılama çıktısı manuel inceleme gerektiriyor.", "Komut tamamlandı fakat çıktı otomatik yorumlanmıyor.", "Onarım kararı vermeden önce kaydedilmiş çıktıyı manuel inceleyin."),
        ["file-system-status-unknown"] = new("Dosya sistemi dirty bit durumu belirlenemedi.", "fsutil çıktısı tanınmadı.", "Kaydedilmiş fsutil çıktısını manuel inceleyin."),
        ["component-store-incomplete"] = new("DISM sağlık denetimi eksik çıktı üretti.", "CheckHealth ve ScanHealth çıktılarının ikisi de alınamadı.", "DISM çıktısını manuel inceleyip tanılamayı yeniden çalıştırın."),
        ["component-store-warning"] = new("DISM bileşen deposunda onarılabilir bozulma bildirdi.", "En az bir DISM denetimi onarılabilir bozulma bildirdi.", "Raporu inceleyin; uygunsa yönlendirmeli DISM ve SFC onarımını çalıştırın."),
        ["component-store-status-unknown"] = new("DISM sağlık sonucu otomatik belirlenemedi.", "Bir veya daha fazla DISM çıktısı tanınmadı.", "Onarım kararı vermeden önce DISM çıktısını manuel inceleyin."),
        ["system-files-status-unknown"] = new("SFC sağlık sonucu otomatik belirlenemedi.", "SFC çıktısı desteklenen bir dilde tanınmadı.", "Onarım kararı vermeden önce SFC çıktısını manuel inceleyin."),
        ["disk-health-unknown"] = new("Disk sağlık sonucu otomatik belirlenemedi.", "Fiziksel disk verisi eksik veya yapılandırılmış biçimde değil.", "Disk durumunu manuel inceleyin ve önemli verileri yedekleyin."),
        ["disk-health-critical"] = new("Bir fiziksel disk sağlıksız veya çalışmaz durumda.", "Get-PhysicalDisk, Unhealthy veya OK olmayan çalışma durumu bildirdi.", "Verileri hemen yedekleyin ve üreticinin disk tanı aracını çalıştırın.")
    };

    private static readonly Dictionary<string, string> TurkishOperationTitles = new()
    {
        ["dirty-bit"] = "Dosya sistemi dirty bit durumu", ["dism-health"] = "Bileşen deposu sağlığı", ["sfc-verify"] = "Sistem dosyası doğrulaması", ["physical-disks"] = "Fiziksel disk sağlığı", ["update-status"] = "Windows Update durumu", ["network-status"] = "Ağ durumu"
    };

    private sealed record Translation(string Summary, string Evidence, string Recommendation);
}
