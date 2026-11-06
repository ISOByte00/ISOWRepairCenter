using ISOWRepairCenter.Core;

namespace ISOWRepairCenter.ViewModels;

public sealed class OperationViewModel : ObservableObject
{
    private bool _isSelected;
    private string _language = "tr";
    public RepairOperation Operation { get; }
    public string Title => _language == "tr" && Turkish.TryGetValue(Operation.Id, out var text) ? text.Title : Operation.Title;
    public string Description => _language == "tr" && Turkish.TryGetValue(Operation.Id, out var text) ? text.Description : Operation.Description;
    public string Risk => _language == "tr" && Turkish.TryGetValue(Operation.Id, out var text) ? text.Risk : Operation.Risk;
    public bool RequiresRestart => Operation.RequiresRestart;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public OperationViewModel(RepairOperation operation) { Operation=operation; IsSelected=false; }
    public void SetLanguage(string language) { _language=language; OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(Risk)); }

    private static readonly Dictionary<string, (string Title, string Description, string Risk)> Turkish = new()
    {
        ["system-overview"]=("Sistem özeti","Windows sürümü, yüklü güncellemeler ve mantıksal birimler.","Salt okunur tanılama."),
        ["physical-disks"]=("Fiziksel disk sağlığı","Disk ortam türü, çalışma durumu ve depolama sağlığı.","Salt okunur tanılama."),
        ["dirty-bit"]=("Dosya sistemi dirty bit durumu","Sistem biriminin onarım için işaretli olup olmadığını denetler.","Salt okunur tanılama."),
        ["chkdsk-scan"]=("Çevrimiçi NTFS taraması","Sistem biriminde zarar vermeyen CHKDSK çevrimiçi taramasını çalıştırır.","Salt okunur tanılama."),
        ["dism-health"]=("Bileşen deposu sağlığı","Dosya değiştirmeden DISM CheckHealth ve ScanHealth çalıştırır.","Salt okunur tanılama."),
        ["sfc-verify"]=("Sistem dosyası doğrulaması","Sistem dosyalarını onarmadan SFC doğrulaması çalıştırır.","Salt okunur tanılama."),
        ["storage-events"]=("Depolama ve NTFS olayları","Son disk, NTFS, depolama ve birim yöneticisi olaylarını toplar.","Salt okunur tanılama."),
        ["critical-events"]=("Kritik sistem olayları","Son kritik ve hata düzeyindeki System olaylarını toplar.","Salt okunur tanılama."),
        ["update-status"]=("Windows Update durumu","Windows Update servis durumunu ve istemci olaylarını denetler.","Salt okunur tanılama."),
        ["network-status"]=("Ağ durumu","Ağ bağdaştırıcılarını, IP yapılandırmasını, DNS'i ve proxy durumunu alır.","Salt okunur tanılama."),
        ["boot-diagnostics"]=("Önyükleme tanılaması","BCD yapılandırmasını ve SrtTrail günlüğünü değiştirmeden okur.","Salt okunur tanılama."),
        ["system-file-repair"]=("Sistem imajı ve dosyalarını onar","Önce DISM, ardından SFC /scannow çalıştırır.","Windows bileşen deposunu ve korunan sistem dosyalarını değiştirir."),
        ["network-reset"]=("Ağ yığınını sıfırla","Winsock, TCP/IP, DNS önbelleği ve WinHTTP proxy ayarlarını sıfırlar.","Özel proxy ve ağ ayarlarını kaldırır; yeniden başlatma gerekir."),
        ["component-cleanup"]=("Bileşen deposunu temizle","Yerine geçen bileşen deposu dosyalarını temizler.","Yerine geçen güncelleme bileşenlerini kaldırır; işlemi kesmeyin."),
        ["update-reset"]=("Windows Update bileşenlerini sıfırla","Update servislerini durdurur, eski önbellekleri yeniden adlandırır ve servisleri başlatır.","İndirilen güncellemeler silinir; yeniden başlatma gerekir."),
        ["chkdsk-fix"]=("CHKDSK onarımını planla","Sistem birimi için sonraki yeniden başlatmada CHKDSK /f planlar.","Dosya sistemi metaverisini onarır; Windows denetimi bitirmelidir."),
        ["bcd-backup"]=("BCD yedekle ve doğrula","Mevcut BCD deposunu dışa aktarır ve kayıtları doğrular.","Geri dönüş yedeği oluşturur; etkin önyükleme yapılandırmasını değiştirmez."),
    };
}
