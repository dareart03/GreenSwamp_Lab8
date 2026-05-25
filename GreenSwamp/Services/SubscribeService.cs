using CsvHelper;
using System.Globalization;

namespace GreenSwamp.Services
{
    public class SubscribeService : ISubscribeService
    {
        private readonly string _csvPath;

        public SubscribeService(IWebHostEnvironment env)
        {
            _csvPath = Path.Combine(env.ContentRootPath, "Data", "subscribers.csv");

            var dir = Path.GetDirectoryName(_csvPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_csvPath))
            {
                using var writer = new StreamWriter(_csvPath);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                csv.WriteHeader<SubscriberRecord>();
                csv.NextRecord();
            }
        }

        public async Task SaveSubscriberAsync(string email)
        {
            var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };

            await using var stream = File.Open(_csvPath, FileMode.Append);
            await using var writer = new StreamWriter(stream);
            await using var csv = new CsvWriter(writer, config);

            csv.WriteRecord(new SubscriberRecord { Email = email, SubscribedAt = DateTime.UtcNow });
            await csv.NextRecordAsync();
        }

        private class SubscriberRecord
        {
            public string Email { get; set; } = string.Empty;
            public DateTime SubscribedAt { get; set; }
        }
    }
}
