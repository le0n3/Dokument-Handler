using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailKit.Search;
using MimeKit;

namespace Dokument_Handler.Services;

public class EmailImportOptions
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Security { get; set; } = "Auto";
    public bool AllowInvalidCertificate { get; set; } = false;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "INBOX";
    /// <summary>Poll interval in seconds (default: 60)</summary>
    public int PollIntervalSeconds { get; set; } = 60;
}

public class EmailImportService : BackgroundService
{
    private readonly ILogger<EmailImportService> _logger;
    private readonly DocumentService _docService;
    private readonly AppSettingsService _settingsService;

    public EmailImportService(
        ILogger<EmailImportService> logger,
        DocumentService docService,
        AppSettingsService settingsService)
    {
        _logger = logger;
        _docService = docService;
        _settingsService = settingsService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailImportService gestartet.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _settingsService.GetEmailImportOptions();
            var delaySeconds = Math.Max(5, options.PollIntervalSeconds);

            try
            {
                if (!options.Enabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.Username))
                {
                    _logger.LogWarning("EmailImportService: IMAP-Konfiguration unvollständig (Host/Username). Import pausiert.");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                    continue;
                }

                await PollMailboxAsync(options, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailImportService: Fehler beim Abrufen der E-Mails.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task PollMailboxAsync(EmailImportOptions options, CancellationToken ct)
    {
        using var client = new ImapClient();
        var socketOptions = ResolveSocketOptions(options);

        if (options.AllowInvalidCertificate)
        {
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
            _logger.LogWarning("EmailImportService: Zertifikatsprüfung ist deaktiviert (nur Testbetrieb).");
        }

        _logger.LogInformation(
            "EmailImportService: Verbinde zu IMAP {Host}:{Port} mit Security={Security}.",
            options.Host,
            options.Port,
            socketOptions);

        await client.ConnectAsync(options.Host, options.Port, socketOptions, ct);
        await client.AuthenticateAsync(options.Username, options.Password, ct);

        var folder = await client.GetFolderAsync(options.Mailbox, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        // Fetch only unseen messages
        var uids = await folder.SearchAsync(SearchQuery.NotSeen, ct);

        if (uids.Count == 0)
        {
            await client.DisconnectAsync(true, ct);
            return;
        }

        _logger.LogInformation("EmailImportService: {Count} neue E-Mail(s) gefunden.", uids.Count);

        var messages = await folder.FetchAsync(uids, MessageSummaryItems.Full | MessageSummaryItems.Body, ct);

        foreach (var summary in messages)
        {
            var message = (MimeMessage)await folder.GetMessageAsync(summary.UniqueId, ct);
            await ProcessMessageAsync(message, ct);

            // Mark as seen so it won't be imported again
            await folder.AddFlagsAsync(summary.UniqueId, MessageFlags.Seen, true, ct);
        }

        await client.DisconnectAsync(true, ct);
    }

    private async Task ProcessMessageAsync(MimeMessage message, CancellationToken ct)
    {
        var attachments = message.Attachments.ToList();

        if (attachments.Count == 0)
        {
            _logger.LogInformation("EmailImportService: E-Mail von {From} hat keine Anhänge – wird übersprungen.", message.From);
            return;
        }

        var description = BuildDescription(message);

        foreach (var attachment in attachments)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = attachment.ContentDisposition?.FileName
                           ?? attachment.ContentType.Name
                           ?? "anhang";

            var contentType = $"{attachment.ContentType.MediaType}/{attachment.ContentType.MediaSubtype}";

            using var memStream = new MemoryStream();

            if (attachment is MimePart part)
            {
                await part.Content.DecodeToAsync(memStream, ct);
            }
            else
            {
                // Multipart attachment (e.g. message/rfc822) – serialize as .eml
                await attachment.WriteToAsync(memStream, ct);
                if (!fileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
                    fileName += ".eml";
                contentType = "message/rfc822";
            }

            memStream.Position = 0;

            await _docService.UploadEmailAttachmentAsync(memStream, fileName, contentType, description);

            _logger.LogInformation("EmailImportService: Anhang '{File}' von '{From}' importiert.", fileName, message.From);
        }
    }

    private static string BuildDescription(MimeMessage message)
    {
        var parts = new List<string>();

        var from = message.From.ToString();
        if (!string.IsNullOrWhiteSpace(from))
            parts.Add($"Von: {from}");

        if (!string.IsNullOrWhiteSpace(message.Subject))
            parts.Add($"Betreff: {message.Subject}");

        parts.Add($"Datum: {message.Date:dd.MM.yyyy HH:mm}");

        return string.Join(" | ", parts);
    }

    private static SecureSocketOptions ResolveSocketOptions(EmailImportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Security)
            && Enum.TryParse<SecureSocketOptions>(options.Security, true, out var parsed))
        {
            return parsed;
        }

        return options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
    }
}
