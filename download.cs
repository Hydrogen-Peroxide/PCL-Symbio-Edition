using System;
using System.IO;
using System.Net;
using System.Text;

class Program
{
    static void Main()
    {
        using var wc = new WebClient();
        wc.Encoding = Encoding.UTF8;
        var content = wc.DownloadString("https://raw.githubusercontent.com/PCL-Community/PCL-CE/dev/Plain%20Craft%20Launcher%202/FormMain.xaml.cs");
        File.WriteAllText(@"C:\Users\XouYa\OneDrive\Desktop\PCL-Symbio Edition\Plain Craft Launcher 2\FormMain.xaml.cs", content, Encoding.UTF8);
        Console.WriteLine($"Written {content.Length} chars");
    }
}