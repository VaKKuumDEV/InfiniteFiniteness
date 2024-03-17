namespace InfiniteFiniteness.Util
{
    public static class Utils
    {
        public const string DIALOGS_FOLDER = "Dialogs";

        public static string GetAppDir(string? subDir = null)
        {
            DirectoryInfo dir = new(Environment.CurrentDirectory + "/Data");
            if (!dir.Exists) dir.Create();

            if(subDir != null)
            {
                dir = new(dir.FullName + "/" + subDir);
                if (!dir.Exists) dir.Create();
            }

            return dir.FullName;
        }

        public static string GetDialogsFolder() => GetAppDir(DIALOGS_FOLDER);
    }
}
