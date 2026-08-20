using Calendar.Services.Email;
using Microsoft.Extensions.Options;
using Xunit;

namespace Calendar.Tests;

public sealed class SmtpEmailSenderTests
{
    [Theory]
    [InlineData("", "app-password")]
    [InlineData("syuzanminasyan08@gmail.com", "")]
    public async Task SendAsync_WithMissingCredentials_ReportsConfigurationErrorBeforeConnecting(
        string userName,
        string password)
    {
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions
        {
            Enabled = true,
            Host = "smtp.gmail.com",
            Port = 587,
            Security = false,
            FromAddress = "syuzanminasyan08@gmail.com",
            UserName = userName,
            Password = password
        }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new EmailMessage("collaborator@luma.test", "Test", "Test")));

        Assert.Equal("SMTP username and password must be configured.", exception.Message);
    }
}
