using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;
using MimeKit;

namespace GitNanny.Email;

static class GraphSender
{
    private static readonly string AuthRecordPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitNanny", "auth_record.json");

    public static async Task SendAsync(MimeMessage mimeMessage, string clientId)
    {
        var credential  = await BuildCredentialAsync(clientId);
        var graphClient = new GraphServiceClient(credential, ["Mail.Send"]);

        var toRecipients = mimeMessage.To
            .OfType<MailboxAddress>()
            .Select(m => new Recipient { EmailAddress = new EmailAddress { Address = m.Address } })
            .ToList();

        var sendMailBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject      = mimeMessage.Subject,
                Body         = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content     = mimeMessage.HtmlBody ?? ""
                },
                ToRecipients = toRecipients
            },
            SaveToSentItems = false
        };

        await graphClient.Me.SendMail.PostAsync(sendMailBody);
    }

    private static async Task<InteractiveBrowserCredential> BuildCredentialAsync(string clientId)
    {
        // Load a previously saved AuthenticationRecord so subsequent runs can authenticate
        // silently without opening the browser. The record identifies the account; the actual
        // tokens (including refresh token) live in the MSAL cache managed by
        // TokenCachePersistenceOptions, which uses DPAPI encryption on Windows.
        AuthenticationRecord? authRecord = null;
        if (File.Exists(AuthRecordPath))
        {
            await using var readStream = File.OpenRead(AuthRecordPath);
            authRecord = await AuthenticationRecord.DeserializeAsync(readStream);
        }

        var credOptions = new InteractiveBrowserCredentialOptions
        {
            ClientId                     = clientId,
            TenantId                     = "common",
            AuthenticationRecord         = authRecord,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name                        = "GitNanny",
                UnsafeAllowUnencryptedStorage = false
            },
            RedirectUri = new Uri("http://localhost")
        };

        var credential = new InteractiveBrowserCredential(credOptions);

        // First run only: authenticate interactively and persist the record.
        if (authRecord is null)
        {
            var record = await credential.AuthenticateAsync(
                new TokenRequestContext(["Mail.Send"]));

            Directory.CreateDirectory(Path.GetDirectoryName(AuthRecordPath)!);
            await using var writeStream = File.Create(AuthRecordPath);
            await record.SerializeAsync(writeStream);
        }

        return credential;
    }
}
