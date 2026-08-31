# 📂 Dokument-Handler

Ein moderner **Dokumenten-Manager mit Blazor (.NET 10)** für lokale Ablage, Kategorisierung, Suche und KI-gestützte Analyse von PDFs.

## ✨ Features

- Upload von Dokumenten (PDF, Bilder, Office, ZIP, ...)
- Lesbarer Dateiname beim Upload (statt kryptischer Scanner-Namen)
- KI-gestützter Namensvorschlag für PDFs
- Kategorien, Tags und Beschreibungen
- Schnellsuche über alle hochgeladenen Dokumente
- Vorschau für PDF- und Bilddateien
- Dokumente bearbeiten und löschen
- Optionaler E-Mail-Import über IMAP
- Optionales KI-Batch-Tagging für PDFs

## 🧰 Tech Stack

- **.NET 10**
- **Blazor** (Interactive Server + WebAssembly-Komponenten)
- **ASP.NET Core Web API**
- **MailKit** (E-Mail-Import)
- **UglyToad.PdfPig** (PDF-Text-Extraktion)

## 🚀 Schnellstart

### Voraussetzungen

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Optional: OpenAI-kompatibles LLM-Backend (z. B. Ollama), wenn KI genutzt werden soll

### Starten

```bash
export DOKUMENT_HANDLER_PASSWORD='ein-langes-eigenes-passwort'
dotnet restore
dotnet run --project Dokument_Handler/Dokument_Handler.csproj
```

Ohne konfiguriertes Passwort verweigert die Anwendung jede Anmeldung. Alternativ kann unter
`Authentication:PasswordHash` ein mit `AuthService.HashPassword(...)` erzeugter PBKDF2-Hash
hinterlegt werden. Das frühere Standardpasswort `admin` existiert nicht mehr.

## ⚙️ Konfiguration

Wichtige Bereiche in `Dokument_Handler/appsettings.json`:

- `AiClassification`
- `EmailImport`

`Storage:MaxTotalSizeBytes` begrenzt den gesamten Dokumentbestand standardmäßig auf 10 GiB.
Der Wert `0` deaktiviert dieses Limit.

> Für öffentliche Repositories: Keine echten Zugangsdaten/Passwörter/API-Keys committen.

Passwörter und API-Schlüssel, die über die Einstellungsseite gespeichert werden, werden mit
ASP.NET Core Data Protection geschützt. Für Serverbetrieb werden Umgebungsvariablen empfohlen:

```bash
export DOKUMENT_HANDLER_PASSWORD='...'
export DOKUMENT_HANDLER_IMAP_PASSWORD='...'
export DOKUMENT_HANDLER_AI_API_KEY='...'
```

Die Anwendung sollte ausschließlich hinter HTTPS betrieben werden. Sämtliche Seiten und APIs
erfordern eine Anmeldung; Login, Upload- und KI-Endpunkte sind zusätzlich rate-limitiert.

## 📁 Struktur

```text
Dokument_Handler/
├─ Dokument_Handler/
│  ├─ Components/Pages/
│  ├─ Controllers/
│  ├─ Services/
│  ├─ Models/
│  └─ DocumentStorage/
├─ README.md
└─ LICENSE
```

## 📄 Lizenz

Dieses Projekt steht unter der **MIT-Lizenz**. Siehe [LICENSE](LICENSE).
