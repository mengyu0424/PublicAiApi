using System;
using System.Globalization;
using System.IO;

namespace ApiStudio
{
    internal sealed class SplitterPositionStore
    {
        private readonly string directory;
        private readonly string preferenceName;

        public SplitterPositionStore(string directory, string preferenceName)
        {
            this.directory = directory;
            this.preferenceName = preferenceName;
        }

        private string FilePath(bool maximized)
        {
            return Path.Combine(directory, preferenceName + (maximized ? "-maximized.txt" : "-normal.txt"));
        }

        public int Load(bool maximized)
        {
            try
            {
                int position;
                if (int.TryParse(File.ReadAllText(FilePath(maximized)), NumberStyles.Integer, CultureInfo.InvariantCulture, out position) && position > 0)
                    return position;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return 0;
        }

        public void Save(bool maximized, int position)
        {
            Directory.CreateDirectory(directory);
            string path = FilePath(maximized);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, position.ToString(CultureInfo.InvariantCulture));
                try { File.Replace(temp, path, null); }
                catch (FileNotFoundException) { File.Move(temp, path); }
            }
            finally { File.Delete(temp); }
        }
    }
}
