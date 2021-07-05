namespace WebApi.Controllers
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.SignalR;

    public class EventHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}
