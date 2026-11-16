# DualCursor

Her ekrana ayrı bir imleç (macOS + Windows). Fareyi ekranlar arasında sürüklemek yerine tek tuşla geçiş yapın.

- Gerçek imleç **aktif ekrana kilitlenir**, kenardan öbür ekrana kaçmaz.
- Diğer ekranlarda yarı saydam bir **hayalet imleç** en son bıraktığınız yerde bekler.
- Kısayola bastığınızda imleç anında o noktaya ışınlanır, eski yerinde hayalet kalır.
- 2'den fazla ekran varsa sırayla döner.

Varsayılan kısayol: **Ctrl + Alt (Option) + Space**

## macOS

Gereksinim: macOS 12+, Xcode Command Line Tools (`xcode-select --install`).

```bash
cd macos
swift build -c release
.build/release/DualCursor
```

İlk açılışta **Erişilebilirlik** izni istenir (Sistem Ayarları → Gizlilik ve Güvenlik → Erişilebilirlik). Terminalden çalıştırıyorsanız izni Terminal'e verin. İzin gelene kadar menü çubuğunda `⇄ !` görünür, izin verilince `⇄` olur.

Ayar dosyası: `~/.config/dualcursor/config.json`

## Windows

Gereksinim: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
cd windows
dotnet run -c Release
# veya tek exe:
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Ek izin gerekmez. Sistem tepsisinde simgesi görünür. Sağ tıklayınca "Çıkış" ve ayarlar çıkar.

Ayar dosyası: `%APPDATA%\DualCursor\config.json`

## Kısayolu değiştirme

```json
{
  "key": "f13",
  "modifiers": []
}
```

- `key`: `a`–`z`, `0`–`9`, `f1`–`f20`, `space`, `tab`, `return`, `escape`, `grave`
- `modifiers`: `ctrl`, `alt`, `shift`, `cmd` (Windows'ta Win tuşu). Boş da olabilir; F13 gibi boşta duran bir tuş için idealdir.

Değişiklikten sonra uygulamayı yeniden başlatın.

## Nasıl çalışıyor?

| Parça | macOS | Windows |
|---|---|---|
| Hayalet imleç | Şeffaf, tıklamayı geçiren `NSWindow` | Katmanlı, tıklamayı geçiren `Form` |
| Ekrana kilitleme | `CGEventTap` + `CGWarpMouseCursorPosition` | `ClipCursor` (+ kilidi koruyan zamanlayıcı) |
| Kısayol | Carbon `RegisterEventHotKey` | `RegisterHotKey` |

## Lisans

MIT
