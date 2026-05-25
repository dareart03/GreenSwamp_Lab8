using CsvHelper;
using GreenSwamp.Pages;
using System.Globalization;

namespace GreenSwamp.Services
{
    public class ContactService : IContactService
    {
        private readonly string _csvPath;

        public ContactService(IWebHostEnvironment env)
        {
            _csvPath = Path.Combine(env.ContentRootPath, "Data", "contacts.csv");

            var dir = Path.GetDirectoryName(_csvPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_csvPath))
            {
                using var writer = new StreamWriter(_csvPath);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                csv.WriteHeader<ContactFormModel>();
                csv.NextRecord();
            }
        }

        public async Task SaveContactAsync(ContactFormModel model)
        {
            var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };

            await using var stream = File.Open(_csvPath, FileMode.Append);
            await using var writer = new StreamWriter(stream);
            await using var csv = new CsvWriter(writer, config);

            csv.WriteRecord(model);
            await csv.NextRecordAsync();
        }
    }
}
