namespace WebApi.Services
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Newtonsoft.Json;

    public static class WeixinMessageSender
    {
        public static async Task SendAsync(this Message message, string url)
        {
            await SendAsync(message, new[] { url }.ToDictionary(_ => _, _ => _));
        }

        public static async Task SendAsync(this Message message, Dictionary<string, string> urls)
        {
            using var wc = new WebClient();
            wc.Headers.Add(HttpRequestHeader.ContentType, "application/json");
            var json = JsonConvert.SerializeObject(message);
            foreach (var url in urls)
            {
                await wc.UploadStringTaskAsync(url.Value, json);
            }
        }
    }

    public record Message(string msgtype);
    public record MarkdownMessage(MarkdownMessageBody markdown) : Message("markdown")
    {
        public MarkdownMessage(string content) : this(new MarkdownMessageBody(content)) { }
    }
    public record MarkdownMessageBody(string content);
    public record TextMessage(TextMessageBody text) : Message("text");
    public record TextMessageBody(string content, string[] mentioned_list = null, string[] mentioned_mobile_list = null);
}