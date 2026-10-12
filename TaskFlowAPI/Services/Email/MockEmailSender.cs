using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace TaskFlowAPI.Services.Email;

public class MockEmailSender : IEmailSender
{
    private readonly ILogger<MockEmailSender> _logger;

    public MockEmailSender(ILogger<MockEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
    {
        // Output the full simulated email transaction envelope straight into your server terminal log feed
        _logger.LogInformation("====================================================");
        _logger.LogInformation("MOCK EMAIL SERVICE DISPATCH SIMULATOR");
        _logger.LogInformation("To: {ToEmail}", toEmail);
        _logger.LogInformation("Subject: {Subject}", subject);
        _logger.LogInformation("Message Content:\n{HtmlMessage}", htmlMessage);
        _logger.LogInformation("====================================================");

        return Task.CompletedTask;
    }
}
