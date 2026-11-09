using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ISOWRepairCenter.Localization;

public sealed class LocalizedText : INotifyPropertyChanged
{
    private string _language = "tr";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language { get => _language; set { if (_language == value) return; _language=value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); } }
    public string this[string key] => Values.TryGetValue(Language, out var language) && language.TryGetValue(key, out var value) ? value : Values["en"].GetValueOrDefault(key, key);
    public string Get(string key) => this[key];

    private static readonly Dictionary<string, Dictionary<string, string>> Values = new()
    {
        ["en"] = new()
        {
            ["WindowTitle"]="ISOWRepairCenter", ["AppTitle"]="ISOByte ISOWRepairCenter", ["Subtitle"]="Select only the diagnostics or repair actions you need. Every command is shown before it runs.", ["Language"]="Language", ["About"]="About", ["AboutText"]="ISOWRepairCenter\nVersion 0.5.0\n\nWindows diagnostics and controlled repair utility.\n\nCopyright © 2026 ISOByte. All rights reserved.", ["Copyright"]="Copyright © 2026 ISOByte. All rights reserved.",
            ["Dashboard"]="Dashboard", ["Diagnostics"]="Diagnostics", ["SafeRepairs"]="Safe Repairs", ["ExpertRepairs"]="Expert Repairs", ["Status"]="Status", ["SessionStorage"]="Session storage",
            ["SessionText"]="All command output, JSON results, and HTML/TXT reports are written under ProgramData.", ["OpenSession"]="Open last session", ["Cancel"]="Cancel active operation", ["SearchDiagnostics"]="Search diagnostics", ["RunDiagnostics"]="Run selected diagnostics",
            ["DismSource"]="Optional DISM repair source", ["DismHint"]="Leave blank to let DISM use Windows Update. If that fails, provide a compatible mounted ISO/WIM/ESD source.", ["RunSafe"]="Run selected safe repairs",
            ["ExpertWarning"]="Expert operations can affect boot, disk, or Windows Update state.", ["BcdHint"]="BCDBoot requires the Windows directory (for example C:\\Windows) and an EFI partition drive letter mounted by the user (for example S:).", ["WindowsSource"]="Windows source", ["EfiTarget"]="EFI target", ["RunExpert"]="Review and run expert repairs", ["Footer"]="No operation starts automatically. Review the command preview before confirming.",
            ["Ready"]="Ready. Select only the checks or repairs you want to run.", ["NoSession"]="No session has been run yet.", ["AdminEnabled"]="Administrator: enabled", ["AdminUnavailable"]="Administrator: unavailable", ["SelectOperation"]="Select at least one operation.", ["RequiredInput"]="Required input", ["RestoreStart"]="Creating a restore point before expert operations.", ["RestoreUnavailable"]="Windows could not create a restore point. BCD backups will still be created when applicable. Continue with the selected expert operations?", ["RestoreTitle"]="Restore point unavailable", ["Running"]="Running", ["LastSession"]="Last session", ["Cancelled"]="Cancelled. Review the session report.", ["Completed"]="Completed successfully. Review the session report.", ["CompletedWarnings"]="Completed with warnings or errors. Review the session report.", ["ExpertConfirm"]="Expert repairs can change boot, disk, or update state. Confirm that you have saved important work.", ["ExpertTitle"]="Expert repair acknowledgement", ["Preview"]="The following selected commands will run:", ["Continue"]="Continue?", ["PreviewTitle"]="Command preview", ["CloseRunning"]="An operation is still running. Cancel it and close?"
        },
        ["tr"] = new()
        {
            ["WindowTitle"]="ISOWRepairCenter", ["AppTitle"]="ISOByte ISOWRepairCenter", ["Subtitle"]="Yalnızca ihtiyacınız olan tanılama veya onarım işlemlerini seçin. Her komut çalışmadan önce gösterilir.", ["Language"]="Dil", ["About"]="Hakkında", ["AboutText"]="ISOWRepairCenter\nSürüm 0.5.0\n\nWindows tanılama ve kontrollü onarım aracı.\n\nTelif Hakkı © 2026 ISOByte. Tüm hakları saklıdır.", ["Copyright"]="Telif Hakkı © 2026 ISOByte. Tüm hakları saklıdır.",
            ["Dashboard"]="Genel Bakış", ["Diagnostics"]="Tanılama", ["SafeRepairs"]="Güvenli Onarımlar", ["ExpertRepairs"]="Uzman Onarımlar", ["Status"]="Durum", ["SessionStorage"]="Oturum kayıtları",
            ["SessionText"]="Komut çıktıları, JSON sonuçları ve HTML/TXT raporları ProgramData altında saklanır.", ["OpenSession"]="Son oturumu aç", ["Cancel"]="Etkin işlemi iptal et", ["SearchDiagnostics"]="Tanılamalarda ara", ["RunDiagnostics"]="Seçili tanılamaları çalıştır",
            ["DismSource"]="İsteğe bağlı DISM onarım kaynağı", ["DismHint"]="DISM'in Windows Update kullanması için boş bırakın. Başarısız olursa uyumlu bağlanmış ISO/WIM/ESD kaynağı belirtin.", ["RunSafe"]="Seçili güvenli onarımları çalıştır",
            ["ExpertWarning"]="Uzman işlemleri önyükleme, disk veya Windows Update durumunu değiştirebilir.", ["BcdHint"]="BCDBoot için Windows dizini (örneğin C:\\Windows) ve kullanıcı tarafından bağlanmış EFI bölüm sürücü harfi (örneğin S:) gerekir.", ["WindowsSource"]="Windows kaynağı", ["EfiTarget"]="EFI hedefi", ["RunExpert"]="Uzman onarımlarını incele ve çalıştır", ["Footer"]="Hiçbir işlem otomatik başlamaz. Onaylamadan önce komut önizlemesini inceleyin.",
            ["Ready"]="Hazır. Yalnızca çalıştırmak istediğiniz tanılama veya onarım işlemlerini seçin.", ["NoSession"]="Henüz bir oturum çalıştırılmadı.", ["AdminEnabled"]="Yönetici: etkin", ["AdminUnavailable"]="Yönetici: kullanılamıyor", ["SelectOperation"]="En az bir işlem seçin.", ["RequiredInput"]="Gerekli bilgi", ["RestoreStart"]="Uzman işlemlerden önce geri yükleme noktası oluşturuluyor.", ["RestoreUnavailable"]="Windows geri yükleme noktası oluşturamadı. Uygun işlemlerde BCD yedeği yine oluşturulacak. Seçilen uzman işlemlerine devam edilsin mi?", ["RestoreTitle"]="Geri yükleme noktası yok", ["Running"]="Çalışıyor", ["LastSession"]="Son oturum", ["Cancelled"]="İptal edildi. Oturum raporunu inceleyin.", ["Completed"]="Tamamlandı. Oturum raporunu inceleyin.", ["CompletedWarnings"]="Uyarı veya hatayla tamamlandı. Oturum raporunu inceleyin.", ["ExpertConfirm"]="Uzman onarımları önyükleme, disk veya Windows Update durumunu değiştirebilir. Önemli çalışmalarınızı kaydettiğinizi onaylayın.", ["ExpertTitle"]="Uzman onarımı onayı", ["Preview"]="Seçilen komutlar çalıştırılacak:", ["Continue"]="Devam edilsin mi?", ["PreviewTitle"]="Komut önizlemesi", ["CloseRunning"]="Bir işlem hâlâ çalışıyor. İptal edilip kapatılsın mı?"
        }
    };
}
