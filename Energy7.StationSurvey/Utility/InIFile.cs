using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Energy7.StationSurvey.Utility
{
    [SupportedOSPlatform("windows")]
    public class InIFile
    {
        [DllImport("kernel32")] private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
        [DllImport("kernel32")] private static extern int  GetPrivateProfileString(string section, string key, string defaultVal, StringBuilder result, int size, string fileName);
        [DllImport("kernel32")] private static extern int  GetPrivateProfileString(string section, int key, string defaultVal, [MarshalAs(UnmanagedType.LPArray)] byte[] result, int size, string fileName);
        [DllImport("kernel32")] private static extern int  GetPrivateProfileString(int section, string key, string defaultVal, [MarshalAs(UnmanagedType.LPArray)] byte[] result, int size, string fileName);

        public void IniWriteValue(string section, string key, string value, string path)
            => WritePrivateProfileString(section, key, value, path);

        public string IniReadValue(string section, string key, string path)
        {
            var temp = new StringBuilder(255);
            GetPrivateProfileString(section, key, "", temp, 255, path);
            return temp.ToString();
        }

        public string[] GetSectionNames(string path)
        {
            for (int max = 500; ; max *= 2)
            {
                var bytes = new byte[max];
                int size  = GetPrivateProfileString(0, "", "", bytes, max, path);
                if (size < max - 2)
                    return Encoding.ASCII.GetString(bytes, 0, size - (size > 0 ? 1 : 0)).Split('\0');
            }
        }

        public string[] GetEntryKeyNames(string section, string path)
        {
            for (int max = 500; ; max *= 2)
            {
                var bytes = new byte[max];
                int size  = GetPrivateProfileString(section, 0, "", bytes, max, path);
                if (size < max - 2)
                    return Encoding.ASCII.GetString(bytes, 0, size - (size > 0 ? 1 : 0)).Split('\0');
            }
        }

        public string GetEntryKeyValue(string section, string entry, string path)
        {
            for (int max = 250; ; max *= 2)
            {
                var result = new StringBuilder(max);
                int size   = GetPrivateProfileString(section, entry, "", result, max, path);
                if (size < max - 1) return result.ToString();
            }
        }
    }
}
