using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;
using MimeKit;

namespace GitReport.Email;

static class GraphSender
{
    private static readonly string AuthRecordPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitReport", "auth_record.json");

    public static async Task SendAsync(MimeMessage mimeMessage, string clientId)
    {
        var credential  = await BuildCredentialAsync(clientId);
        var graphClient = new GraphServiceClient(credential, ["Mail.Send"]);

        var toAddress = mimeMessage.To
            .OfType<MailboxAddress>()
            .Select(m => m.Address)
            .First();

        var sendMailBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = mimeMessage.Subject,
                Body    = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content     = mimeMessage.HtmlBody ?? ""
                },
                ToRecipients =
                [
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = toAddress }
                    }
                ]
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
                Name                        = "GitReport",
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
