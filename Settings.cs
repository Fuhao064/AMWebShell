namespace AMWebShell;

/// <summary>极简 key=value 设置文件，避免引入额外依赖。</summary>
internal sealed class Settings
{
    private readonly string _file;
    public bool CloseToTray = true;
    public bool CloseHintShown;
    public Rectangle? Bounds;
    public bool Maximized;
    public double Zoom = 1.0;

    public Settings(string dir)
    {
        _file = Path.Combine(dir, "settings.ini");
        try
        {
            foreach (var line in File.ReadAllLines(_file))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var k = line[..i].Trim(); var v = line[(i + 1)..].Trim();
                switch (k)
                {
                    case "CloseToTray": CloseToTray = v == "1"; break;
                    case "CloseHintShown": CloseHintShown = v == "1"; break;
                    case "Maximized": Maximized = v == "1"; break;
                    case "Zoom": double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Zoom); break;
                    case "Bounds":
                        var p = v.Split(',').Select(int.Parse).ToArray();
                        if (p.Length == 4 && p[2] > 100 && p[3] > 100) Bounds = new Rectangle(p[0], p[1], p[2], p[3]);
                        break;
                }
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var b = Bounds;
            File.WriteAllLines(_file, new[]
            {
                $"CloseToTray={(CloseToTray ? 1 : 0)}",
                $"CloseHintShown={(CloseHintShown ? 1 : 0)}",
                $"Maximized={(Maximized ? 1 : 0)}",
                "Zoom=" + Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b is { } r ? $"Bounds={r.X},{r.Y},{r.Width},{r.Height}" : "",
            });
        }
        catch { }
    }
}
