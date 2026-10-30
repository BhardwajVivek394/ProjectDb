using System.IO;

namespace Energy7.StationSurvey.Utility
{
    public class FileReader
    {
        private readonly string _filePath;

        public FileReader(string filePath) => _filePath = filePath;

        public string Read()
        {
            using var reader = new StreamReader(_filePath);
            return reader.ReadToEnd();
        }
    }
}
