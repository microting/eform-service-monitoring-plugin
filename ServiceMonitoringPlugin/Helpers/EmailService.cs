namespace ServiceMonitoringPlugin.Helpers
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using SendGrid;
    using SendGrid.Helpers.Mail;

    public class EmailService
    {
        private readonly string _sendGridApiKey;
        private readonly string _fromEmailAddress;
        private readonly string _fromEmailName;

        public EmailService(
            string sendGridApiKey,
            string fromEmailAddress,
            string fromEmailName)
        {
            _sendGridApiKey = sendGridApiKey;
            _fromEmailAddress = fromEmailAddress;
            _fromEmailName = fromEmailName;
        }

        public async Task SendAsync(string subject, string to, string text = null, string html = null)
        {
            try
            {
                var client = new SendGridClient(_sendGridApiKey);
                var fromAddress = new EmailAddress(_fromEmailAddress, _fromEmailName);
                var toAddress = new EmailAddress(to);
                var msg = MailHelper.CreateSingleEmail(fromAddress, toAddress, subject, text, html);
                var response = await client.SendEmailAsync(msg);
                if (((int)response.StatusCode < 200) || ((int)response.StatusCode >= 300))
                {
                    var responseText = await response.Body.ReadAsStringAsync();
                    throw new Exception($"Status: {response.StatusCode}. Response: {responseText}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to send email message", ex);
            }
        }

        public async Task SendFileAsync(string subject, string to, string fileName, string text = null, string html = null)
        {
            try
            {
                var client = new SendGridClient(_sendGridApiKey);
                var fromEmail = new EmailAddress(_fromEmailAddress, _fromEmailName);
                var toEmail = new EmailAddress(to);
                var msg = MailHelper.CreateSingleEmail(fromEmail, toEmail, subject, text, html);
                var bytes = File.ReadAllBytes(fileName);
                var file = Convert.ToBase64String(bytes);
                msg.AddAttachment(Path.GetFileName(fileName), file);
                var response = await client.SendEmailAsync(msg);
                if (((int)response.StatusCode < 200) || ((int)response.StatusCode >= 300))
                {
                    throw new Exception($"Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to send email message", ex);
            }
            finally
            {
                File.Delete(fileName);
            }
        }
    }
}
