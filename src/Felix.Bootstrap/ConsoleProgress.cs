namespace Felix.Bootstrap;

/// <summary>Affiche l'avancement dans la console (texte + barre de progression).</summary>
public sealed class ConsoleProgress : IProgress<BootProgress>
{
    private string _lastStatus = "";
    private long _lastTicks;
    private bool _barActive;

    public void Report(BootProgress p)
    {
        var now = Environment.TickCount64;
        var statusChanged = p.Status != _lastStatus;
        if (!statusChanged && now - _lastTicks < 100 && (p.Fraction ?? 0) < 1)
            return;
        _lastTicks = now;

        if (Console.IsOutputRedirected)
        {
            if (statusChanged)
                Console.WriteLine(p.Status);
            _lastStatus = p.Status;
            return;
        }

        if (statusChanged)
        {
            if (_barActive)
                Console.WriteLine();
            Console.WriteLine(p.Status);
            _lastStatus = p.Status;
            _barActive = false;
        }

        if (p.Fraction is { } fraction)
        {
            var width = 30;
            var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * width);
            var line = $"  [{new string('█', filled)}{new string('░', width - filled)}] {fraction * 100,3:0} %  {p.Detail}";
            Console.Write("\r" + line.PadRight(Math.Max(line.Length, 78)));
            _barActive = true;
        }
    }

    public void Finish()
    {
        if (_barActive)
            Console.WriteLine();
        _barActive = false;
    }
}
