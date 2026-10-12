using System.Threading.Tasks;

namespace TaskFlowAPI.Services.Email;

public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string htmlMessage);
}
