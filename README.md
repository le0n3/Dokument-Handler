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
dotnet restore
dotnet run --project Dokument_Handler/Dokument_Handler.csproj
```

## ⚙️ Konfiguration

Wichtige Bereiche in `Dokument_Handler/appsettings.json`:

- `AiClassification`
- `EmailImport`

> Für öffentliche Repositories: Keine echten Zugangsdaten/Passwörter/API-Keys committen.

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