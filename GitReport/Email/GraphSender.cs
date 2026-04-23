using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;
using MimeKit;

namespace GitReport.Email;

static class GraphSender
{
    public static async Task SendAsync(MimeMessage mimeMessage, string clientId)
    {
        var credOptions = new InteractiveBrowserCredentialOptions
        {
            ClientId = clientId,
            TenantId = "common",
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "GitReport",
                UnsafeAllowUnencryptedStorage = false
            },
            RedirectUri = new Uri("http://localhost")
        };

        var credential = new InteractiveBrowserCredential(credOptions);
        var graphClient = new GraphServiceClient(credential, ["Mail.Send"]);

        var htmlBody = mimeMessage.HtmlBody ?? "";
        var toAddress = mimeMessage.To
            .OfType<MailboxAddress>()
            .Select(m => m.Address)
            .First();

        var sendMailBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = mimeMessage.Subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content     = htmlBody
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
}
